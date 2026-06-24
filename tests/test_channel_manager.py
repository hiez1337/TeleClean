"""Tests for ChannelManager — filtering and sorting logic."""

from __future__ import annotations

from unittest.mock import MagicMock, patch

import pytest

from app.core.channel_manager import ChannelManager, LeaveProgressCallback
from app.models.channel import Channel
from app.core.telegram_client import LeaveResult


class TestFiltering:
    """ChannelManager.filter_* / search methods."""

    def test_search_filters_by_title(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        manager._channels = sample_channels
        result = manager.search("python")
        assert len(result) == 1
        assert result[0].title == "Python Developers"

    def test_search_empty_query_returns_all(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        manager._channels = sample_channels
        result = manager.search("")
        assert len(result) == len(sample_channels)

    def test_search_case_insensitive(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        manager._channels = sample_channels
        result = manager.search("TECH")
        assert len(result) == 1
        assert result[0].title == "Tech News"

    def test_filter_unread(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        result = manager.filter_unread(sample_channels)
        assert len(result) == 5  # Tech News, Python Developers, Music Lovers, Dev Chat, Test Bot
        for ch in result:
            assert ch.unread_count > 0

    def test_filter_low_subscribers(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        result = manager.filter_low_subscribers(sample_channels, max_count=100)
        # Channels with <= 100 subscribers: Abandoned (15), Music Lovers (80), Empty (0), Old Group (8), Test Bot (0), Deleted User (0)
        assert len(result) == 6
        for ch in result:
            assert ch.participant_count <= 100

    def test_filter_low_subscribers_default(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        result = manager.filter_low_subscribers(sample_channels)
        # Default max_count=100: same 6 channels
        assert len(result) == 6


class TestSorting:
    """ChannelManager.sort_* methods."""

    def test_sort_by_title(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        sorted_list = manager.sort_by_title(sample_channels)
        titles = [ch.title for ch in sorted_list]
        assert titles == sorted(titles, key=str.lower)

    def test_sort_by_title_reverse(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        sorted_list = manager.sort_by_title(sample_channels, reverse=True)
        titles = [ch.title for ch in sorted_list]
        assert titles == sorted(titles, key=str.lower, reverse=True)

    def test_sort_by_subscribers(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        sorted_list = manager.sort_by_subscribers(sample_channels)
        counts = [ch.participant_count for ch in sorted_list]
        assert counts == sorted(counts)


class TestBulkLeave:
    """ChannelManager.bulk_leave — success, error, and interruption."""

    @pytest.mark.asyncio
    async def test_all_success(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        for ch in sample_channels:
            manager._client.set_leave_result(ch.id, LeaveResult.SUCCESS)  # type: ignore[attr-defined]

        progress = []
        done = []

        callbacks = LeaveProgressCallback(
            on_progress=lambda c, t, title: progress.append((c, t)),
            on_channel_done=lambda cid, title, status: done.append((cid, status)),
            on_complete=lambda ok, err: None,
            on_stop=lambda: False,
        )

        await manager.bulk_leave(sample_channels[:2], callbacks, delay_between=0.01)
        assert len(done) == 2
        assert all(status == "success" for _, status in done)

    @pytest.mark.asyncio
    async def test_stop_interrupts(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        for ch in sample_channels:
            manager._client.set_leave_result(ch.id, LeaveResult.SUCCESS)  # type: ignore[attr-defined]

        stop_called = False

        def stop() -> bool:
            nonlocal stop_called
            stop_called = True
            return True

        done = []
        callbacks = LeaveProgressCallback(
            on_progress=lambda c, t, title: None,
            on_channel_done=lambda cid, title, status: done.append((cid, status)),
            on_complete=lambda ok, err: None,
            on_stop=stop,
        )

        await manager.bulk_leave(sample_channels, callbacks, delay_between=0.01)
        assert stop_called
        # Should have processed at most 1 channel before stopping
        assert len(done) <= 1

    @pytest.mark.asyncio
    async def test_leave_rate_limited(self, manager: ChannelManager, sample_channels: list[Channel]) -> None:
        """Rate-limited channels should be counted as errors."""
        for i, ch in enumerate(sample_channels):
            result = LeaveResult.RATE_LIMITED if i == 0 else LeaveResult.SUCCESS
            manager._client.set_leave_result(ch.id, result)  # type: ignore[attr-defined]

        done = []
        callbacks = LeaveProgressCallback(
            on_progress=lambda c, t, title: None,
            on_channel_done=lambda cid, title, status: done.append((cid, status)),
            on_complete=lambda ok, err: None,
            on_stop=lambda: False,
        )

        await manager.bulk_leave(sample_channels[:2], callbacks, delay_between=0.01, rate_limit_delay=0.01)
        statuses = [s for _, s in done]
        assert "rate_limited" in statuses
        assert "success" in statuses
