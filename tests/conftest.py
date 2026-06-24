"""Shared fixtures for TeleClean tests."""

from __future__ import annotations

from typing import Any

import pytest
from pytest import fixture

from app.core.telegram_client import LeaveResult
from app.models.channel import Channel
from app.core.channel_manager import ChannelManager


@fixture
def sample_channels() -> list[Channel]:
    """Return a list of fake channel objects for testing."""
    return [
        Channel(id=-1001, title="Tech News", username="technews", participant_count=15000, unread_count=3),
        Channel(id=-1002, title="Cooking Recipes", username=None, participant_count=500, unread_count=0),
        Channel(id=-1003, title="Abandoned Channel", username="dead", participant_count=15, unread_count=0),
        Channel(id=-1004, title="Python Developers", username="pythondev", participant_count=4200, unread_count=12),
        Channel(id=-1005, title="Music Lovers", username="music", participant_count=80, unread_count=1),
        Channel(id=-1006, title="Empty Channel", username=None, participant_count=0, unread_count=0),
    ]


class FakeTelegramClient:
    """A mock implementation of TelegramClientWrapper for testing.

    Does not require a running event loop.
    """

    def __init__(self) -> None:
        self._authorized = False
        self._leave_results: dict[int, Any] = {}

    def set_leave_result(self, channel_id: int, result) -> None:
        self._leave_results[channel_id] = result

    async def leave_channel(self, channel_id: int) -> LeaveResult:
        return self._leave_results.get(channel_id, LeaveResult.SUCCESS)


@fixture
def fake_client() -> FakeTelegramClient:
    return FakeTelegramClient()


@fixture
def manager(fake_client: FakeTelegramClient) -> ChannelManager:
    return ChannelManager(fake_client)  # type: ignore[arg-type]
