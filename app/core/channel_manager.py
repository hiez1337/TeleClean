"""Channel manager — business logic for listing, filtering, and bulk-leaving.

Handles caching, filtering, and progress persistence for bulk-leave
operations so interrupted runs can be resumed.
"""

from __future__ import annotations

import asyncio
import json
import logging
import time
from pathlib import Path
from typing import Callable, Optional

from app.models.channel import Channel
from app.core.telegram_client import (
    LeaveResult,
    TelegramClientWrapper,
)
from app.services.session_service import get_data_dir, load_leave_history, save_leave_history

logger = logging.getLogger(__name__)


class LeaveProgressCallback:
    """Callback interface for leave-progress updates from the GUI."""

    def __init__(
        self,
        on_progress: Callable[[int, int, str], None],
        on_channel_done: Callable[[int, str, str], None],
        on_complete: Callable[[int, int], None],
        on_stop: Callable[[], bool],
    ) -> None:
        self.on_progress = on_progress
        self.on_channel_done = on_channel_done
        self.on_complete = on_complete
        self.on_stop = on_stop


class ChannelManager:
    """Manages channel listing, caching, filtering, and bulk-leave operations."""

    def __init__(self, client: TelegramClientWrapper) -> None:
        self._client = client
        self._channels: list[Channel] = []
        self._cache_path: Path = get_data_dir() / "channel_cache.json"
        self._running: bool = False

    # ------------------------------------------------------------------
    # Loading / caching
    # ------------------------------------------------------------------

    async def load_channels(
        self,
        force_refresh: bool = False,
        on_progress: Optional[Callable[[int, int], None]] = None,
    ) -> list[Channel]:
        """Return the channel list, optionally refreshing from Telegram.

        When *force_refresh* is True (or no cache exists), fetches from the
        API and writes the result to a local cache file.  Otherwise the
        cached list is returned immediately.
        """
        if not force_refresh and self._channels:
            return self._channels

        if not force_refresh:
            cached = self._load_cache()
            if cached is not None:
                self._channels = cached
                return cached

        # Fetch from Telegram
        channels = await self._client.get_all_channels(
            on_progress=on_progress
        )
        self._channels = channels
        self._save_cache(channels)
        return channels

    def _load_cache(self) -> Optional[list[Channel]]:
        """Load channel cache from disk, or return None if unavailable."""
        if not self._cache_path.exists():
            return None
        try:
            with open(self._cache_path, encoding="utf-8") as f:
                data = json.load(f)
            return [Channel.from_dict(item) for item in data]
        except (json.JSONDecodeError, OSError, KeyError) as exc:
            logger.warning("Failed to load channel cache: %s", exc)
            return None

    def _save_cache(self, channels: list[Channel]) -> None:
        """Persist channel list to the local cache file."""
        try:
            with open(self._cache_path, "w", encoding="utf-8") as f:
                json.dump(
                    [ch.to_dict() for ch in channels],
                    f,
                    indent=2,
                    ensure_ascii=False,
                )
        except OSError as exc:
            logger.error("Failed to save channel cache: %s", exc)

    def clear_cache(self) -> None:
        """Delete the local channel cache."""
        self._channels = []
        if self._cache_path.exists():
            self._cache_path.unlink()

    # ------------------------------------------------------------------
    # Filtering
    # ------------------------------------------------------------------

    def search(self, query: str) -> list[Channel]:
        """Filter channels whose title contains *query* (case-insensitive)."""
        if not query:
            return self._channels
        q = query.lower()
        return [ch for ch in self._channels if q in ch.title.lower()]

    def filter_unread(self, channels: list[Channel]) -> list[Channel]:
        """Return only channels with unread messages."""
        return [ch for ch in channels if ch.unread_count > 0]

    def filter_low_subscribers(
        self, channels: list[Channel], max_count: int = 100
    ) -> list[Channel]:
        """Return only channels with <= *max_count* subscribers."""
        return [ch for ch in channels if ch.participant_count <= max_count]

    def sort_by_title(self, channels: list[Channel], reverse: bool = False) -> list[Channel]:
        """Sort channels alphabetically by title."""
        return sorted(channels, key=lambda c: c.title.lower(), reverse=reverse)

    def sort_by_date(self, channels: list[Channel], reverse: bool = True) -> list[Channel]:
        """Sort channels by unread count (proxy for activity)."""
        return sorted(channels, key=lambda c: c.unread_count, reverse=reverse)

    def sort_by_subscribers(
        self, channels: list[Channel], reverse: bool = False
    ) -> list[Channel]:
        """Sort channels by participant count."""
        return sorted(channels, key=lambda c: c.participant_count, reverse=reverse)

    # ------------------------------------------------------------------
    # Bulk leave
    # ------------------------------------------------------------------

    @property
    def is_running(self) -> bool:
        """Whether a bulk-leave operation is currently in progress."""
        return self._running

    async def bulk_leave(
        self,
        channels: list[Channel],
        callbacks: LeaveProgressCallback,
        delay_between: float = 3.0,
        rate_limit_delay: float = 30.0,
    ) -> None:
        """Leave a list of channels sequentially.

        Parameters
        ----------
        channels : list[Channel]
            The channels to leave.
        callbacks : LeaveProgressCallback
            GUI callbacks for progress reporting and stop checking.
        delay_between : float
            Seconds to wait between consecutive leave requests.
        rate_limit_delay : float
            Extra seconds to wait when a rate-limit is hit.
        """
        self._running = True
        total = len(channels)
        success_count = 0
        error_count = 0

        history = load_leave_history()
        history_entry = {
            "started_at": time.time(),
            "total": total,
            "completed": 0,
            "success": 0,
            "errors": [],
        }
        history.append(history_entry)

        for i, channel in enumerate(channels):
            if not self._running:
                logger.info("Bulk leave interrupted by user")
                break

            if callbacks.on_stop():
                logger.info("Bulk leave stopped by user")
                break

            callbacks.on_progress(i + 1, total, channel.title)
            result = await self._client.leave_channel(channel.id)

            if result == LeaveResult.SUCCESS:
                success_count += 1
                callbacks.on_channel_done(channel.id, channel.title, "success")
            elif result == LeaveResult.RATE_LIMITED:
                error_count += 1
                callbacks.on_channel_done(
                    channel.id, channel.title, "rate_limited"
                )
                logger.info("Rate limited, waiting %.0fs", rate_limit_delay)
                await asyncio.sleep(rate_limit_delay)
            else:
                error_count += 1
                status = result.value if isinstance(result, LeaveResult) else "error"
                callbacks.on_channel_done(channel.id, channel.title, status)

            # Update history
            history_entry["completed"] = i + 1
            history_entry["success"] = success_count
            if result != LeaveResult.SUCCESS:
                history_entry["errors"].append(
                    {"channel_id": channel.id, "title": channel.title, "error": result.value}
                )
            save_leave_history(history)

            if result == LeaveResult.SUCCESS:
                await asyncio.sleep(delay_between)

        history_entry["finished_at"] = time.time()
        save_leave_history(history)

        self._running = False
        callbacks.on_complete(success_count, error_count)

    def stop(self) -> None:
        """Signal the running bulk-leave operation to stop."""
        self._running = False
