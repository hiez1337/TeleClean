"""Tests for the Channel data model."""

from __future__ import annotations

from app.models.channel import Channel


class TestChannelCreation:
    """Verify Channel construction and defaults."""

    def test_minimal(self) -> None:
        ch = Channel(id=-100, title="Test")
        assert ch.id == -100
        assert ch.title == "Test"
        assert ch.username is None
        assert ch.participant_count == 0
        assert ch.unread_count == 0
        assert ch.is_channel is True
        assert ch.is_joined is True

    def test_full(self) -> None:
        ch = Channel(
            id=-200,
            title="Full Channel",
            username="@full",
            participant_count=1000,
            unread_count=5,
            is_channel=True,
            is_joined=True,
        )
        assert ch.username == "@full"
        assert ch.participant_count == 1000
        assert ch.unread_count == 5


class TestChannelSerialization:
    """Verify to_dict / from_dict round-trip."""

    def test_round_trip(self) -> None:
        original = Channel(
            id=-100,
            title="Tech News",
            username="technews",
            participant_count=15000,
            unread_count=3,
        )
        data = original.to_dict()
        restored = Channel.from_dict(data)
        assert restored == original
        assert restored.id == original.id
        assert restored.title == original.title
        assert restored.username == original.username
        assert restored.participant_count == original.participant_count

    def test_from_dict_minimal(self) -> None:
        data = {"id": -999, "title": "Minimal"}
        ch = Channel.from_dict(data)
        assert ch.id == -999
        assert ch.title == "Minimal"
        assert ch.participant_count == 0
        assert ch.is_joined is True
