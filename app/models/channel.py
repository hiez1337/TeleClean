"""Channel data model for TeleClean."""

from __future__ import annotations

from dataclasses import dataclass, field, asdict
from typing import Optional


@dataclass
class Channel:
    """Represents a Telegram channel that the user can leave."""

    id: int
    """Telegram dialog/chat ID (may be negative)."""

    title: str
    """Display name of the channel."""

    username: Optional[str] = None
    """Public @username, if available."""

    participant_count: int = 0
    """Number of subscribers/members."""

    unread_count: int = 0
    """Number of unread messages."""

    is_channel: bool = True
    """True if this is a channel (vs a group or personal chat)."""

    is_joined: bool = True
    """Whether the user is still a member."""

    def to_dict(self) -> dict:
        """Serialize to a JSON-compatible dictionary."""
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "Channel":
        """Create a Channel from a dictionary (e.g. loaded from JSON)."""
        return cls(
            id=data["id"],
            title=data["title"],
            username=data.get("username"),
            participant_count=data.get("participant_count", 0),
            unread_count=data.get("unread_count", 0),
            is_channel=data.get("is_channel", True),
            is_joined=data.get("is_joined", True),
        )
