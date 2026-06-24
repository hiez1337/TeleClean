"""Session persistence service for TeleClean.

Handles saving and loading Telethon session files and application settings
(.teleclean/config.json).
"""

from __future__ import annotations

import json
import logging
import os
from pathlib import Path
from typing import Any, Optional

logger = logging.getLogger(__name__)


def get_data_dir() -> Path:
    """Return the path to the app data directory (``.teleclean/``).

    Creates the directory if it does not exist.
    """
    data_dir = Path(os.path.dirname(os.path.abspath(__file__))).parent.parent / ".teleclean"
    data_dir.mkdir(parents=True, exist_ok=True)
    return data_dir


def get_session_path(session_name: str = "teleclean") -> str:
    """Return the absolute path for a Telethon session file (without extension)."""
    return str(get_data_dir() / session_name)


def load_config() -> dict[str, Any]:
    """Load UI settings from ``.teleclean/config.json``.

    Returns an empty dict when the file does not exist or is corrupted.
    """
    config_path = get_data_dir() / "config.json"
    if not config_path.exists():
        return {}
    try:
        with open(config_path, encoding="utf-8") as f:
            return json.load(f)
    except (json.JSONDecodeError, OSError) as exc:
        logger.warning("Failed to load config: %s", exc)
        return {}


def save_config(config: dict[str, Any]) -> None:
    """Persist UI settings to ``.teleclean/config.json``."""
    config_path = get_data_dir() / "config.json"
    try:
        with open(config_path, "w", encoding="utf-8") as f:
            json.dump(config, f, indent=2, ensure_ascii=False)
    except OSError as exc:
        logger.error("Failed to save config: %s", exc)


def load_leave_history() -> list[dict[str, Any]]:
    """Load previously saved leave-progress history."""
    path = get_data_dir() / "leave_history.json"
    if not path.exists():
        return []
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (json.JSONDecodeError, OSError) as exc:
        logger.warning("Failed to load leave history: %s", exc)
        return []


def save_leave_history(history: list[dict[str, Any]]) -> None:
    """Persist leave-progress history."""
    path = get_data_dir() / "leave_history.json"
    try:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(history, f, indent=2, ensure_ascii=False)
    except OSError as exc:
        logger.error("Failed to save leave history: %s", exc)
