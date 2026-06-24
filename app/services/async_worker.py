"""Asyncio worker that runs in a background QThread.

Provides a bridge between PySide6's GUI thread and Telethon's async
operations so the UI stays responsive during network calls.
"""

from __future__ import annotations

import asyncio
import logging
from concurrent.futures import CancelledError, Future
from typing import Any, Awaitable, Callable, Optional

from PySide6.QtCore import QObject, QThread, Qt, Signal, Slot

logger = logging.getLogger(__name__)


class AsyncWorkerSignals(QObject):
    """Signals emitted by AsyncWorker."""

    started = Signal()
    finished = Signal()
    error = Signal(str)


class AsyncWorker(QThread):
    """QThread that runs an asyncio event loop.

    Ensures all result/error callbacks are dispatched to the Qt main
    thread via a queued signal, so GUI updates are always thread-safe.

    Usage
    -----
    .. code-block:: python

        worker = AsyncWorker()
        worker.start()

        # Schedule a coroutine from any thread
        future = worker.run_coroutine(some_async_function())
        result = future.result()  # blocks calling thread

        worker.stop()
        worker.wait()
    """

    # Internal signal for dispatching callbacks to the main thread.
    # Carries a zero-arg callable that the main-thread slot will invoke.
    _callback_dispatch = Signal(object)

    def __init__(self, parent: Optional[QObject] = None) -> None:
        super().__init__(parent)
        self._loop: Optional[asyncio.AbstractEventLoop] = None
        self._running = False

        self.signals = AsyncWorkerSignals()

        # Connect the dispatch signal with a queued connection so that
        # ``emit`` from the worker thread queues the callable to run on
        # the main thread.
        self._callback_dispatch.connect(
            self._on_callback,
            Qt.QueuedConnection,
        )

    @Slot(object)
    def _on_callback(self, callback: Callable[[], None]) -> None:
        """Execute a callback on the Qt main thread."""
        try:
            callback()
        except Exception as exc:
            logger.exception("Main-thread callback error: %s", exc)

    def run(self) -> None:
        """QThread entry point: start the asyncio event loop."""
        self._running = True
        self._loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self._loop)
        self.signals.started.emit()
        try:
            self._loop.run_forever()
        finally:
            # Cancel all pending tasks
            pending = asyncio.all_tasks(self._loop)
            for task in pending:
                task.cancel()
            if pending:
                self._loop.run_until_complete(
                    asyncio.gather(*pending, return_exceptions=True)
                )
            self._loop.close()
            self._running = False

    def stop(self) -> None:
        """Stop the event loop safely from any thread."""
        if self._loop and self._loop.is_running():
            self._loop.call_soon_threadsafe(self._loop.stop)

    def run_coroutine(self, coro: Awaitable[Any]) -> Future:
        """Schedule a coroutine on the worker's event loop from any thread.

        Returns a ``concurrent.futures.Future`` that can be used to wait
        for the result.
        """
        if not self._loop or not self._loop.is_running():
            raise RuntimeError("Worker event loop is not running")
        return asyncio.run_coroutine_threadsafe(coro, self._loop)

    def run_and_emit(
        self,
        coro: Awaitable[Any],
        on_result: Optional[Callable[[Any], None]] = None,
        on_error: Optional[Callable[[Exception], None]] = None,
    ) -> Future:
        """Schedule a coroutine and route result/error to callbacks.

        Both *on_result* and *on_error* are always dispatched on the Qt
        **main thread**, making them safe for GUI updates.
        """
        future = self.run_coroutine(coro)

        def _done(f: Future) -> None:
            try:
                result = f.result()
                if on_result is not None:
                    self._callback_dispatch.emit(
                        lambda r=result: on_result(r)
                    )
            except CancelledError:
                logger.debug("Async operation cancelled")
            except Exception as exc:
                logger.exception("Async operation failed")
                if on_error is not None:
                    # Capture exc in closure via default argument (PEP 626)
                    self._callback_dispatch.emit(
                        lambda e=exc: on_error(e)
                    )

        future.add_done_callback(_done)
        return future

    @property
    def loop(self) -> Optional[asyncio.AbstractEventLoop]:
        return self._loop

    @property
    def is_running(self) -> bool:
        return self._running
