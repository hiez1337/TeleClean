"""Asyncio worker that runs in a background QThread.

Provides a bridge between PySide6's GUI thread and Telethon's async
operations so the UI stays responsive during network calls.
"""

from __future__ import annotations

import asyncio
import logging
from concurrent.futures import Future
from typing import Any, Awaitable, Callable, Optional

from PySide6.QtCore import QObject, QThread, Signal

logger = logging.getLogger(__name__)


class AsyncWorkerSignals(QObject):
    """Signals emitted by AsyncWorker."""

    started = Signal()
    finished = Signal()
    error = Signal(str)


class AsyncWorker(QThread):
    """QThread that runs an asyncio event loop.

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

    def __init__(self, parent: Optional[QObject] = None) -> None:
        super().__init__(parent)
        self._loop: Optional[asyncio.AbstractEventLoop] = None
        self.signals = AsyncWorkerSignals()
        self._running = False

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

        The callbacks are dispatched on the **caller's thread** (normally
        the Qt main thread).
        """
        future = self.run_coroutine(coro)

        def _done(f: Future) -> None:
            try:
                result = f.result()
                if on_result is not None:
                    on_result(result)
            except Exception as exc:
                logger.exception("Async operation failed")
                if on_error is not None:
                    on_error(exc)

        future.add_done_callback(_done)
        return future

    @property
    def loop(self) -> Optional[asyncio.AbstractEventLoop]:
        return self._loop

    @property
    def is_running(self) -> bool:
        return self._running
