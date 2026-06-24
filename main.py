#!/usr/bin/env python3
"""TeleClean — Desktop application for bulk-leaving Telegram channels.

Usage
-----
    python main.py

Requires a ``.env`` file with ``TELEGRAM_API_ID`` and ``TELEGRAM_API_HASH``.
"""

from __future__ import annotations

import logging
import sys

from dotenv import load_dotenv
from PySide6.QtWidgets import QApplication

from app.gui.main_window import MainWindow

# ── Logging ──────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%H:%M:%S",
)

logger = logging.getLogger(__name__)


def main() -> None:
    """Application entry point."""
    load_dotenv()

    app = QApplication(sys.argv)
    app.setApplicationName("TeleClean")
    app.setApplicationVersion("1.0.0")

    # Apply dark theme by default (overridden by saved setting in MainWindow)
    try:
        from pathlib import Path
        qss_path = Path(__file__).parent / "app" / "gui" / "styles" / "telegram_dark.qss"
        if qss_path.exists():
            with open(qss_path, encoding="utf-8") as f:
                app.setStyleSheet(f.read())
    except Exception:
        pass

    window = MainWindow()
    window.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
