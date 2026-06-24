"""Main window for TeleClean.

Manages the two main views (authentication → channel list) and the
bulk-leave workflow with progress reporting.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Optional

from PySide6.QtCore import QTimer, Slot
from PySide6.QtGui import QAction
from PySide6.QtWidgets import (
    QApplication,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QMessageBox,
    QProgressBar,
    QPushButton,
    QStackedWidget,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

from app.core.telegram_client import TelegramClientWrapper
from app.core.channel_manager import ChannelManager, LeaveProgressCallback
from app.gui.auth_widget import AuthWidget
from app.gui.channel_list import ChannelListWidget
from app.services.async_worker import AsyncWorker
from app.services.session_service import load_config, save_config

logger = logging.getLogger(__name__)


class MainWindow(QMainWindow):
    """Application main window."""

    def __init__(self) -> None:
        super().__init__()

        self.setWindowTitle("TeleClean")
        self.setMinimumSize(800, 600)
        self.resize(960, 720)

        # --- Async worker (background asyncio event loop) ---
        self._worker = AsyncWorker(self)
        self._worker.signals.error.connect(self._on_worker_error)
        self._worker.start()

        # --- Telegram client ---
        self._client = TelegramClientWrapper()
        self._channel_manager = ChannelManager(self._client)

        # --- State ---
        self._settings = load_config()
        self._leave_running = False

        self._build_ui()
        self._build_menu()

        # Apply saved settings
        self._apply_settings()

        # Start auth flow after a short delay (to let the event loop spin up)
        QTimer.singleShot(500, self._start_auth)

    # ------------------------------------------------------------------
    # UI construction
    # ------------------------------------------------------------------

    def _build_ui(self) -> None:
        """Create the central widget and child views."""
        central = QWidget()
        self.setCentralWidget(central)
        main_layout = QVBoxLayout(central)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)

        # -- Stacked views --
        self._stack = QStackedWidget()
        main_layout.addWidget(self._stack, 1)

        # Page 0: Authentication
        self._auth_widget = AuthWidget(
            self._client,
            run_async=self._run_async,
            parent=self,
        )
        self._auth_widget.authenticated.connect(self._on_authenticated)
        self._stack.addWidget(self._auth_widget)

        # Page 1: Channel list + controls
        self._channel_page = self._build_channel_page()
        self._stack.addWidget(self._channel_page)

        # Start on auth page
        self._stack.setCurrentIndex(0)

        # -- Status bar --
        self._status_label = QLabel("Готово")
        self._status_label.setStyleSheet(
            "padding: 4px 12px; color: #8e9ba4; font-size: 12px;"
        )
        main_layout.addWidget(self._status_label)

    def _build_channel_page(self) -> QWidget:
        """Build the channel-list page with controls and progress area."""
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(16, 8, 16, 8)
        layout.setSpacing(8)

        # Top bar: title + refresh
        top_bar = QHBoxLayout()
        title = QLabel("Мои каналы")
        title.setObjectName("titleLabel")
        top_bar.addWidget(title)

        top_bar.addStretch()

        self._refresh_btn = QPushButton("🔄 Обновить")
        self._refresh_btn.setObjectName("secondaryButton")
        self._refresh_btn.clicked.connect(self._on_refresh)
        top_bar.addWidget(self._refresh_btn)

        layout.addLayout(top_bar)

        # Channel list
        self._channel_list = ChannelListWidget()
        self._channel_list.selection_changed.connect(self._on_selection_changed)
        self._channel_list.leave_requested.connect(self._on_leave_requested)
        layout.addWidget(self._channel_list, 1)

        # Bottom controls
        controls = QHBoxLayout()
        controls.setSpacing(8)

        # Leave button
        self._leave_btn = QPushButton("🚪 Выйти из выбранных")
        self._leave_btn.setObjectName("dangerButton")
        self._leave_btn.setEnabled(False)
        self._leave_btn.clicked.connect(self._confirm_and_leave)
        controls.addWidget(self._leave_btn)

        controls.addStretch()

        # Theme toggle
        self._theme_btn = QPushButton("🌙 Тёмная тема")
        self._theme_btn.setObjectName("secondaryButton")
        self._theme_btn.clicked.connect(self._toggle_theme)
        controls.addWidget(self._theme_btn)

        layout.addLayout(controls)

        # Progress area (hidden by default)
        self._progress_widget = QWidget()
        self._progress_widget.setVisible(False)
        progress_layout = QVBoxLayout(self._progress_widget)
        progress_layout.setContentsMargins(0, 8, 0, 0)
        progress_layout.setSpacing(4)

        self._progress_bar = QProgressBar()
        self._progress_bar.setRange(0, 100)
        progress_layout.addWidget(self._progress_bar)

        self._progress_label = QLabel("")
        self._progress_label.setObjectName("statusLabel")
        progress_layout.addWidget(self._progress_label)

        # Stop button
        stop_btn_layout = QHBoxLayout()
        stop_btn_layout.addStretch()
        self._stop_btn = QPushButton("⏹ Стоп")
        self._stop_btn.setObjectName("secondaryButton")
        self._stop_btn.clicked.connect(self._on_stop_leave)
        stop_btn_layout.addWidget(self._stop_btn)
        progress_layout.addLayout(stop_btn_layout)

        # Log area
        self._log_area = QTextEdit()
        self._log_area.setReadOnly(True)
        self._log_area.setMaximumHeight(120)
        self._log_area.setStyleSheet(
            "background-color: #242f3d; border-radius: 6px; padding: 8px;"
            " color: #8e9ba4; font-size: 12px; font-family: monospace;"
        )
        progress_layout.addWidget(self._log_area)

        layout.addWidget(self._progress_widget)

        return page

    def _build_menu(self) -> None:
        """Build the application menu bar."""
        menubar = self.menuBar()

        # File menu
        file_menu = menubar.addMenu("Файл")
        restart_action = QAction("Перезапустить авторизацию", self)
        restart_action.triggered.connect(self._restart_auth)
        file_menu.addAction(restart_action)
        file_menu.addSeparator()
        exit_action = QAction("Выход", self)
        exit_action.triggered.connect(self.close)
        file_menu.addAction(exit_action)

        # Settings menu
        settings_menu = menubar.addMenu("Настройки")
        theme_menu = settings_menu.addMenu("Тема")
        self._dark_theme_action = QAction("Тёмная", self)
        self._dark_theme_action.setCheckable(True)
        self._dark_theme_action.triggered.connect(lambda: self._set_theme("dark"))
        theme_menu.addAction(self._dark_theme_action)
        self._light_theme_action = QAction("Светлая", self)
        self._light_theme_action.setCheckable(True)
        self._light_theme_action.triggered.connect(lambda: self._set_theme("light"))
        theme_menu.addAction(self._light_theme_action)

        # Help menu
        help_menu = menubar.addMenu("Помощь")
        about_action = QAction("О программе", self)
        about_action.triggered.connect(self._show_about)
        help_menu.addAction(about_action)

    # ------------------------------------------------------------------
    # Async helpers
    # ------------------------------------------------------------------

    def _run_async(self, coro, callback=None, errback=None):
        """Schedule a coroutine on the worker thread.

        If *callback* is provided, it is called on the Qt main thread with
        the result.  If *errback* is provided, it is called with the
        exception.
        """
        return self._worker.run_and_emit(coro, callback, errback)

    # ------------------------------------------------------------------
    # Authentication flow
    # ------------------------------------------------------------------

    @Slot()
    def _start_auth(self) -> None:
        """Called shortly after startup to begin auth."""
        self._run_async(
            self._client.start_with_session(),
            callback=self._on_session_check_done,
            errback=lambda exc: self._auth_widget._show_error(str(exc)),
        )

    def _on_session_check_done(self, restored: bool) -> None:
        """Called after checking for an existing session."""
        if restored:
            self._on_authenticated()
        else:
            # Start QR auth: connect first, then begin flow
            self._run_async(
                self._client.connect(),
                callback=lambda _: self._auth_widget._start_qr_flow(),
                errback=lambda exc: self._auth_widget._show_error(str(exc)),
            )

    @Slot()
    def _on_authenticated(self) -> None:
        """User has authenticated — switch to channel list."""
        self._status_label.setText("Авторизация успешна. Загружаю каналы...")
        self._run_async(
            self._load_channels_async(),
            callback=self._on_channels_loaded,
            errback=lambda exc: self._status_label.setText(
                f"Ошибка загрузки каналов: {exc}"
            ),
        )

    @Slot()
    def _on_worker_error(self, message: str) -> None:
        logger.error("Worker error: %s", message)

    # ------------------------------------------------------------------
    # Channel loading
    # ------------------------------------------------------------------

    async def _load_channels_async(self) -> list:
        """Async: fetch channels and return them (no GUI calls)."""
        return await self._channel_manager.load_channels(
            force_refresh=True,
        )

    def _on_channels_loaded(self, channels) -> None:
        """Called on the main thread with loaded channels."""
        if not channels:
            self._status_label.setText("Нет доступных каналов")
            return
        self._channel_list.set_channels(channels)
        self._stack.setCurrentIndex(1)
        self._status_label.setText(f"Загружено {len(channels)} каналов")

    @Slot()
    def _on_refresh(self) -> None:
        """Refresh the channel list from Telegram."""
        self._refresh_btn.setEnabled(False)
        self._status_label.setText("Обновление списка каналов...")
        self._run_async(
            self._channel_manager.load_channels(force_refresh=True),
            callback=self._on_refresh_complete,
            errback=lambda exc: self._status_label.setText(
                f"Ошибка обновления: {exc}"
            ),
        )

    def _on_refresh_complete(self, channels) -> None:
        """Called on main thread after refresh."""
        self._channel_list.set_channels(channels)
        self._status_label.setText(f"Загружено {len(channels)} каналов")
        self._refresh_btn.setEnabled(True)

    # ------------------------------------------------------------------
    # Selection
    # ------------------------------------------------------------------

    @Slot(int)
    def _on_selection_changed(self, count: int) -> None:
        self._leave_btn.setEnabled(count > 0)
        if count > 0:
            self._leave_btn.setText(f"🚪 Выйти из {count} каналов")
        else:
            self._leave_btn.setText("🚪 Выйти из выбранных")

    # ------------------------------------------------------------------
    # Bulk leave
    # ------------------------------------------------------------------

    @Slot()
    def _confirm_and_leave(self) -> None:
        """Show confirmation dialog, then start bulk leave."""
        channels = self._channel_list.get_selected_channels()
        if not channels:
            return

        count = len(channels)
        reply = QMessageBox.question(
            self,
            "Подтверждение выхода",
            f"Вы уверены, что хотите выйти из {count} каналов?\n\n"
            "Это действие нельзя отменить. Вы будете удалены из этих каналов.",
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )

        if reply != QMessageBox.Yes:
            return

        self._start_bulk_leave(channels)

    def _start_bulk_leave(self, channels) -> None:
        """Initiate the bulk-leave workflow."""
        self._leave_running = True
        self._leave_btn.setEnabled(False)
        self._channel_list.setEnabled(False)
        self._refresh_btn.setEnabled(False)

        # Show progress area
        self._progress_widget.setVisible(True)
        self._progress_bar.setRange(0, len(channels))
        self._progress_bar.setValue(0)
        self._log_area.clear()
        self._stop_btn.setEnabled(True)

        callbacks = LeaveProgressCallback(
            on_progress=self._update_progress,
            on_channel_done=self._log_channel_result,
            on_complete=self._on_leave_complete,
            on_stop=lambda: not self._leave_running,
        )

        self._run_async(
            self._channel_manager.bulk_leave(channels, callbacks),
            errback=lambda exc: self._on_leave_error(exc),
        )

    def _update_progress(self, current: int, total: int, title: str) -> None:
        """Update progress bar and label (called from worker thread)."""
        self._progress_bar.setValue(current)
        self._progress_label.setText(f"Обрабатывается: {title}  [{current}/{total}]")

    def _log_channel_result(self, channel_id: int, title: str, status: str) -> None:
        """Log the result of leaving one channel (called from worker thread)."""
        status_map = {
            "success": "✅ Успешно",
            "rate_limited": "⏳ Rate limit",
            "forbidden": "⛔ Нет доступа",
            "not_found": "❓ Не найден",
            "error": "❌ Ошибка",
        }
        msg = f"{status_map.get(status, status)}: {title}"
        self._log_area.append(msg)

    @Slot()
    def _on_stop_leave(self) -> None:
        """Stop the bulk-leave operation."""
        self._leave_running = False
        self._channel_manager.stop()
        self._stop_btn.setEnabled(False)
        self._log_area.append("⏹ Остановлено пользователем")

    def _on_leave_complete(self, success: int, errors: int) -> None:
        """Called when the bulk-leave operation finishes."""
        self._leave_running = False
        self._stop_btn.setEnabled(False)
        self._progress_label.setText(f"✅ Завершено: {success} успешно, {errors} с ошибками")

        QMessageBox.information(
            self,
            "Выход завершён",
            f"✅ Успешно: {success}\n"
            f"❌ С ошибками: {errors}\n\n"
            "Обновите список каналов, чтобы увидеть изменения.",
        )

        # Re-enable controls
        self._channel_list.setEnabled(True)
        self._refresh_btn.setEnabled(True)

    def _on_leave_error(self, exc: Exception) -> None:
        """Called on fatal error during bulk leave."""
        self._leave_running = False
        self._stop_btn.setEnabled(False)
        self._log_area.append(f"❌ Критическая ошибка: {exc}")
        self._channel_list.setEnabled(True)
        self._refresh_btn.setEnabled(True)
        QMessageBox.critical(self, "Ошибка", f"Критическая ошибка: {exc}")

    # ------------------------------------------------------------------
    # Theme
    # ------------------------------------------------------------------

    def _apply_settings(self) -> None:
        """Load and apply saved settings (theme)."""
        theme = self._settings.get("theme", "dark")
        self._set_theme(theme)

    def _set_theme(self, theme: str) -> None:
        """Apply the given theme and update menu checks."""
        qss_file = (
            Path(__file__).parent / "styles" / f"telegram_{theme}.qss"
        )
        if qss_file.exists():
            with open(qss_file, encoding="utf-8") as f:
                qss = f.read()
            QApplication.instance().setStyleSheet(qss)

        self._settings["theme"] = theme
        save_config(self._settings)

        self._dark_theme_action.setChecked(theme == "dark")
        self._light_theme_action.setChecked(theme == "light")
        self._theme_btn.setText(
            "☀️ Светлая тема" if theme == "dark" else "🌙 Тёмная тема"
        )

    @Slot()
    def _toggle_theme(self) -> None:
        """Switch between dark and light themes."""
        current = self._settings.get("theme", "dark")
        new_theme = "light" if current == "dark" else "dark"
        self._set_theme(new_theme)

    # ------------------------------------------------------------------
    # Menu actions
    # ------------------------------------------------------------------

    @Slot()
    def _restart_auth(self) -> None:
        """Clear session and restart authorisation."""
        reply = QMessageBox.question(
            self,
            "Сброс авторизации",
            "Вы уверены? Текущая сессия будет удалена, и потребуется "
            "повторная авторизация.",
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )
        if reply != QMessageBox.Yes:
            return

        # Clear session
        self._run_async(self._client.disconnect())

        # Clear session file
        session_path = Path(os.path.dirname(
            os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        )) / ".teleclean" / "teleclean.session"
        if session_path.exists():
            session_path.unlink()

        self._channel_manager.clear_cache()
        self._stack.setCurrentIndex(0)
        self._status_label.setText("Авторизация сброшена")
        QTimer.singleShot(1000, self._start_auth)

    @Slot()
    def _show_about(self) -> None:
        """Show the About dialog."""
        QMessageBox.about(
            self,
            "О программе TeleClean",
            "<h2>TeleClean</h2>"
            "<p>Версия 1.0.0</p>"
            "<p>Приложение для массового выхода из Telegram-каналов.</p>"
            "<p>Использует библиотеку Telethon для работы с Telegram API<br>"
            "и PySide6 для графического интерфейса.</p>",
        )

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def closeEvent(self, event) -> None:
        """Clean up on window close."""
        # Stop the async worker
        if hasattr(self, "_worker"):
            self._worker.stop()
            if not self._worker.wait(3000):
                self._worker.terminate()
        super().closeEvent(event)
