"""Main window for TeleClean.

Manages the two main views (authentication → channel list) and the
bulk-leave workflow with progress reporting.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path

import sys as _sys

from PySide6.QtCore import QTimer, Slot
from PySide6.QtGui import QAction
from PySide6.QtWidgets import (
    QApplication,
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMainWindow,
    QMessageBox,
    QProgressBar,
    QPushButton,
    QStackedWidget,
    QTabWidget,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

from app.core.telegram_client import TelegramClientWrapper
from app.core.channel_manager import ChannelManager, LeaveProgressCallback
from app.gui.auth_widget import AuthWidget
from app.gui.channel_list import ChannelListWidget
from app.models.channel import DIALOG_BOT, DIALOG_CHANNEL, DIALOG_DELETED, DIALOG_GROUP, DIALOG_SUPERGROUP, DIALOG_USER
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
        self._worker.signals.started.connect(self._start_auth)

        # --- Telegram client ---
        self._client = TelegramClientWrapper()
        self._channel_manager = ChannelManager(self._client)

        # --- State ---
        self._settings = load_config()
        self._leave_running = False
        self._load_channels_future = None
        self._dialog_list_widgets: dict[str, ChannelListWidget] = {}

        self._build_ui()
        self._build_menu()

        # Apply saved settings
        self._apply_settings()

        # Start the worker (auth begins once the event loop is ready)
        self._worker.start()

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
        """Build the channel-list page with tabs and controls."""
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(16, 8, 16, 8)
        layout.setSpacing(8)

        # Top bar: title + refresh
        top_bar = QHBoxLayout()
        title = QLabel("Мои диалоги")
        title.setObjectName("titleLabel")
        top_bar.addWidget(title)

        top_bar.addStretch()

        self._refresh_btn = QPushButton("🔄 Обновить")
        self._refresh_btn.setObjectName("secondaryButton")
        self._refresh_btn.clicked.connect(self._on_refresh)
        top_bar.addWidget(self._refresh_btn)

        layout.addLayout(top_bar)

        # Tab widget with per-type lists
        self._tab_widget = QTabWidget()

        self._tab_defs: list[tuple[str, str]] = [
            (DIALOG_CHANNEL, "📺 Каналы"),
            (DIALOG_SUPERGROUP, "👥 Чаты"),
            (DIALOG_BOT, "🤖 Боты"),
            (DIALOG_DELETED, "🗑 Удалённые"),
        ]

        self._tab_index_map: dict[int, str] = {}
        for i, (dtype, label) in enumerate(self._tab_defs):
            w = ChannelListWidget(dialog_type=dtype)
            w.selection_changed.connect(self._on_selection_changed)
            w.leave_requested.connect(self._on_leave_requested)
            self._tab_widget.addTab(w, label)
            self._dialog_list_widgets[dtype] = w
            self._tab_index_map[i] = dtype

        self._tab_widget.currentChanged.connect(self._on_tab_changed)
        layout.addWidget(self._tab_widget, 1)

        # Bottom controls
        controls = QHBoxLayout()
        controls.setSpacing(8)

        self._action_btn = QPushButton("🚪 Выйти из выбранных")
        self._action_btn.setObjectName("dangerButton")
        self._action_btn.setEnabled(False)
        self._action_btn.clicked.connect(self._confirm_and_leave)
        controls.addWidget(self._action_btn)

        controls.addStretch()

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

        stop_btn_layout = QHBoxLayout()
        stop_btn_layout.addStretch()
        self._stop_btn = QPushButton("⏹ Стоп")
        self._stop_btn.setObjectName("secondaryButton")
        self._stop_btn.clicked.connect(self._on_stop_leave)
        stop_btn_layout.addWidget(self._stop_btn)
        progress_layout.addLayout(stop_btn_layout)

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
        settings_menu.addSeparator()
        api_action = QAction("API ключи Telegram", self)
        api_action.triggered.connect(self._show_api_settings)
        settings_menu.addAction(api_action)

        # Help menu
        help_menu = menubar.addMenu("Помощь")
        about_action = QAction("О программе", self)
        about_action.triggered.connect(self._show_about)
        help_menu.addAction(about_action)
        help_menu.addSeparator()
        debug_action = QAction("Информация для отладки", self)
        debug_action.triggered.connect(self._show_debug)
        help_menu.addAction(debug_action)

    # ------------------------------------------------------------------
    # Async helpers
    # ------------------------------------------------------------------

    def _run_async(self, coro, on_result=None, on_error=None):
        """Schedule a coroutine on the worker thread."""
        return self._worker.run_and_emit(
            coro,
            on_result=on_result,
            on_error=on_error,
        )

    # ------------------------------------------------------------------
    # Authentication flow
    # ------------------------------------------------------------------

    @Slot()
    def _start_auth(self) -> None:
        """Called shortly after startup to begin auth."""
        self._run_async(
            self._client.start_with_session(),
            on_result=self._on_session_check_done,
            on_error=lambda exc: self._auth_widget._show_error(str(exc)),
        )

    def _on_session_check_done(self, restored: bool) -> None:
        """Called after checking for an existing session."""
        if restored:
            self._on_authenticated()
        else:
            # Start QR auth: connect first, then begin flow
            self._run_async(
                self._client.connect(),
                on_result=lambda _: self._auth_widget._start_qr_flow(),
                on_error=lambda exc: self._auth_widget._show_error(str(exc)),
            )

    @Slot()
    def _on_authenticated(self) -> None:
        """User has authenticated — switch to channel list."""
        self._status_label.setText("Авторизация успешна. Загружаю каналы...")
        # Store reference so _restart_auth can cancel it
        self._load_channels_future = self._run_async(
            self._load_channels_async(),
            on_result=self._on_channels_loaded,
            on_error=lambda exc: self._status_label.setText(
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
        """Called on the main thread with loaded dialogs."""
        if self._client.auth_state.name != "AUTHENTICATED":
            logger.debug("Skipping _on_channels_loaded — no longer authenticated")
            return
        if not channels:
            self._status_label.setText("Нет доступных диалогов")
            return
        self._distribute_channels(channels)
        self._stack.setCurrentIndex(1)
        total = len(channels)
        counts = ", ".join(
            f"{l}: {len([c for c in channels if c.dialog_type == t])}"
            for t, l in self._tab_defs
        )
        self._status_label.setText(f"Загружено {total} диалогов ({counts})")

    @Slot()
    def _on_refresh(self) -> None:
        """Refresh the channel list from Telegram."""
        self._refresh_btn.setEnabled(False)
        self._status_label.setText("Обновление списка каналов...")
        self._load_channels_future = self._run_async(
            self._channel_manager.load_channels(force_refresh=True),
            on_result=self._on_refresh_complete,
            on_error=lambda exc: self._status_label.setText(
                f"Ошибка обновления: {exc}"
            ),
        )

    def _on_refresh_complete(self, channels) -> None:
        """Called on main thread after refresh."""
        self._distribute_channels(channels)
        total = len(channels)
        self._status_label.setText(f"Загружено {total} диалогов")
        self._refresh_btn.setEnabled(True)

    def _distribute_channels(self, channels: list) -> None:
        """Split loaded dialogs into per-type tab widgets."""
        for dtype, widget in self._dialog_list_widgets.items():
            filtered = [c for c in channels if c.dialog_type == dtype]
            widget.set_channels(filtered)

    # ------------------------------------------------------------------
    # Tab switching
    # ------------------------------------------------------------------

    @Slot(int)
    def _on_tab_changed(self, index: int) -> None:
        dtype = self._tab_index_map.get(index, DIALOG_CHANNEL)
        count = self._dialog_list_widgets[dtype].total_count()
        left = self._dialog_list_widgets[dtype].left_count()
        if dtype == DIALOG_DELETED:
            self._action_btn.setText("🗑 Удалить выбранные")
        else:
            self._action_btn.setText("🚪 Выйти из выбранных")
        self._action_btn.setEnabled(False)

    # ------------------------------------------------------------------
    # Selection
    # ------------------------------------------------------------------

    @Slot(object)
    def _on_selection_changed(self, count: int) -> None:
        self._action_btn.setEnabled(count > 0)
        dtype = self._active_dialog_type()
        if dtype == DIALOG_DELETED:
            self._action_btn.setText(f"🗑 Удалить {count} чатов" if count > 0 else "🗑 Удалить выбранные")
        else:
            self._action_btn.setText(f"🚪 Выйти из {count}" if count > 0 else "🚪 Выйти из выбранных")

    def _active_dialog_type(self) -> str:
        idx = self._tab_widget.currentIndex()
        return self._tab_index_map.get(idx, DIALOG_CHANNEL)

    def _active_list_widget(self) -> ChannelListWidget:
        dtype = self._active_dialog_type()
        return self._dialog_list_widgets[dtype]

    @Slot(list)
    def _on_leave_requested(self, channel_ids: list) -> None:
        """Handle leave_requested signal from channel list."""
        channels = self._active_list_widget().get_selected_channels()
        if channels:
            self._confirm_and_leave()

    # ------------------------------------------------------------------
    # Bulk leave
    # ------------------------------------------------------------------

    @Slot()
    def _confirm_and_leave(self) -> None:
        """Show confirmation dialog, then start bulk leave or delete."""
        widget = self._active_list_widget()
        channels = widget.get_selected_channels()
        if not channels:
            return

        dtype = self._active_dialog_type()
        count = len(channels)

        if dtype == DIALOG_DELETED:
            reply = QMessageBox.question(
                self,
                "Подтверждение удаления",
                f"Удалить {count} чатов с удалёнными аккаунтами?\n\n"
                "Диалоги будут удалены из вашего списка чатов.",
                QMessageBox.Yes | QMessageBox.No,
                QMessageBox.No,
            )
            if reply != QMessageBox.Yes:
                return
            self._start_bulk_delete(channels)
        else:
            label = "каналов" if dtype == DIALOG_CHANNEL else "чатов" if dtype in (DIALOG_SUPERGROUP, DIALOG_GROUP) else "диалогов"
            reply = QMessageBox.question(
                self,
                "Подтверждение выхода",
                f"Вы уверены, что хотите выйти из {count} {label}?\n\n"
                "Это действие нельзя отменить.",
                QMessageBox.Yes | QMessageBox.No,
                QMessageBox.No,
            )
            if reply != QMessageBox.Yes:
                return
            self._start_bulk_leave(channels)

    def _start_bulk_leave(self, channels) -> None:
        """Initiate the bulk-leave workflow."""
        widget = self._active_list_widget()
        self._leave_running = True
        self._action_btn.setEnabled(False)
        widget.setEnabled(False)
        self._refresh_btn.setEnabled(False)

        # Show progress area
        self._progress_widget.setVisible(True)
        self._progress_bar.setRange(0, len(channels))
        self._progress_bar.setValue(0)
        self._log_area.clear()
        self._stop_btn.setEnabled(True)

        callbacks = LeaveProgressCallback(
            on_progress=self._wrap_qt_callback(self._update_progress),
            on_channel_done=self._wrap_qt_callback(self._log_channel_result),
            on_complete=self._wrap_qt_callback(self._on_leave_complete),
            on_stop=lambda: not self._leave_running,
        )

        self._run_async(
            self._channel_manager.bulk_leave(channels, callbacks),
            on_error=lambda exc: self._on_leave_error(exc),
        )

    def _start_bulk_delete(self, channels) -> None:
        """Delete selected dialogs (deleted-account chats)."""
        widget = self._active_list_widget()
        self._leave_running = True
        self._action_btn.setEnabled(False)
        widget.setEnabled(False)
        self._refresh_btn.setEnabled(False)

        self._progress_widget.setVisible(True)
        self._progress_bar.setRange(0, len(channels))
        self._progress_bar.setValue(0)
        self._log_area.clear()
        self._stop_btn.setEnabled(True)

        callbacks = LeaveProgressCallback(
            on_progress=self._wrap_qt_callback(self._update_progress),
            on_channel_done=self._wrap_qt_callback(self._log_delete_result),
            on_complete=self._wrap_qt_callback(self._on_leave_complete),
            on_stop=lambda: not self._leave_running,
        )

        self._run_async(
            self._channel_manager.bulk_leave(channels, callbacks),
            on_error=lambda exc: self._on_leave_error(exc),
        )

    def _wrap_qt_callback(self, func):
        """Return a wrapper that dispatches *func* to the Qt main thread."""
        def wrapper(*args):
            if self._worker.isRunning():
                self._worker._callback_dispatch.emit(lambda: func(*args))
        return wrapper

    def _update_progress(self, current: int, total: int, title: str) -> None:
        """Update progress bar and label (must be called from main thread)."""
        self._progress_bar.setValue(current)
        self._progress_label.setText(f"Обрабатывается: {title}  [{current}/{total}]")

    def _log_channel_result(self, channel_id: int, title: str, status: str) -> None:
        """Log the result of leaving one dialog (must be called from main thread)."""
        status_map = {
            "success": "✅ Успешно",
            "rate_limited": "⏳ Rate limit",
            "forbidden": "⛔ Нет доступа",
            "not_found": "❓ Не найден",
            "error": "❌ Ошибка",
        }
        msg = f"{status_map.get(status, status)}: {title}"
        self._log_area.append(msg)
        if status == "success":
            self._active_list_widget().mark_channels_left([channel_id])

    def _log_delete_result(self, channel_id: int, title: str, status: str) -> None:
        """Log the result of deleting one dialog."""
        status_map = {
            "success": "🗑 Удалён",
            "error": "❌ Ошибка",
        }
        msg = f"{status_map.get(status, status)}: {title}"
        self._log_area.append(msg)
        if status == "success":
            self._active_list_widget().mark_channels_left([channel_id])

    @Slot()
    def _on_stop_leave(self) -> None:
        """Stop the bulk-leave operation."""
        self._leave_running = False
        self._channel_manager.stop()
        self._stop_btn.setEnabled(False)
        self._log_area.append("⏹ Остановлено пользователем")

    def _on_leave_complete(self, success: int, errors: int) -> None:
        """Called when the bulk-leave/delete operation finishes."""
        self._leave_running = False
        self._stop_btn.setEnabled(False)
        self._progress_label.setText(f"✅ Завершено: {success} успешно, {errors} с ошибками")

        dtype = self._active_dialog_type()
        if dtype == DIALOG_DELETED:
            msg = f"🗑 Удалено: {success}\n❌ Ошибок: {errors}\n\nУдалённые диалоги отмечены в списке."
        else:
            msg = f"✅ Успешно: {success}\n❌ С ошибками: {errors}\n\nПокинутые диалоги отмечены в списке. Нажмите «Обновить», чтобы убрать их."

        QMessageBox.information(self, "Операция завершена", msg)

        self._active_list_widget().setEnabled(True)
        self._refresh_btn.setEnabled(True)

    def _on_leave_error(self, exc: Exception) -> None:
        """Called on fatal error during bulk leave."""
        self._leave_running = False
        self._stop_btn.setEnabled(False)
        self._log_area.append(f"❌ Критическая ошибка: {exc}")
        self._active_list_widget().setEnabled(True)
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

        # Cancel any in-flight channel load (may be flood-waiting)
        if self._load_channels_future is not None:
            self._load_channels_future.cancel()
            self._load_channels_future = None

        self._status_label.setText("Отключаюсь от Telegram...")

        # Reset session (log_out + brand-new TelegramClient)
        self._run_async(
            self._client.reset_session(),
            on_result=lambda _: self._on_session_reset(),
        )

    def _on_session_reset(self) -> None:
        """Reset UI and start QR flow after session has been cleared."""
        self._auth_widget.reset_widget()
        self._channel_manager.clear_cache()
        self._stack.setCurrentIndex(0)
        self._status_label.setText("Авторизация сброшена")
        QTimer.singleShot(1000, lambda: self._run_async(
            self._client.connect(),
            on_result=lambda _: self._auth_widget._start_qr_flow(),
            on_error=lambda exc: self._auth_widget._show_error(str(exc)),
        ))

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
    # API Settings
    # ------------------------------------------------------------------

    @Slot()
    def _show_api_settings(self) -> None:
        """Dialog to view/enter Telegram API keys."""
        from app.services.session_service import load_api_keys, save_api_keys

        saved = load_api_keys()

        dlg = QDialog(self)
        dlg.setWindowTitle("API ключи Telegram")
        dlg.setMinimumWidth(420)
        layout = QVBoxLayout(dlg)

        info = QLabel(
            "API ключи нужны для подключения к Telegram.\n"
            "Получить: https://my.telegram.org/apps\n"
            "Встроенные ключи работают по умолчанию.\n"
            "Заполните поля только если они не работают."
        )
        info.setWordWrap(True)
        layout.addWidget(info)

        layout.addWidget(QLabel("API ID:"))
        id_input = QLineEdit()
        id_input.setPlaceholderText("11600115")
        id_input.setText(str(saved.get("api_id", "")))
        layout.addWidget(id_input)

        layout.addWidget(QLabel("API Hash:"))
        hash_input = QLineEdit()
        hash_input.setPlaceholderText("dabf2aadd76c3a56983809d54c0760c6")
        hash_input.setText(saved.get("api_hash", ""))
        layout.addWidget(hash_input)

        layout.addWidget(QLabel(
            "После сохранения перезапустите приложение, чтобы новые ключи вступили в силу."
        ))

        buttons = QDialogButtonBox(QDialogButtonBox.Save | QDialogButtonBox.Cancel)
        buttons.accepted.connect(lambda: _save())
        buttons.rejected.connect(dlg.reject)
        layout.addWidget(buttons)

        def _save():
            aid = id_input.text().strip()
            ah = hash_input.text().strip()
            if aid and ah:
                try:
                    save_api_keys(int(aid), ah)
                    QMessageBox.information(dlg, "Сохранено",
                        "Ключи сохранены. Перезапустите приложение.")
                    dlg.accept()
                except ValueError:
                    QMessageBox.warning(dlg, "Ошибка", "API ID должен быть числом")
            else:
                QMessageBox.warning(dlg, "Ошибка", "Заполните оба поля")

        dlg.exec()

    # ------------------------------------------------------------------
    # Debug
    # ------------------------------------------------------------------

    @Slot()
    def _show_debug(self) -> None:
        """Show a debug-info dialog with system state."""
        lines = []
        def L(k, v):
            lines.append(f"{k}: {v}")

        try:
            L("--- Application ---", "")
            L("Mode", "Frozen (.exe)" if getattr(_sys, 'frozen', False) else "Dev (python)")
            L("Python", _sys.version)
            L("PySide", "imported")

            L("", "")
            L("--- API Keys ---", "")
            from app.services.session_service import load_api_keys
            saved = load_api_keys()
            env_id = os.getenv("TELEGRAM_API_ID")
            if env_id:
                L("Source", "Environment (.env)")
            elif saved.get("api_id"):
                L("Source", "Settings (config.json)")
            else:
                L("Source", "Built-in (telegram_client.py)")
            L("API ID from settings", str(saved.get("api_id", "")) if saved else "not saved")
            L("API Hash from settings", "set ✓" if saved.get("api_hash") else "not saved")

            L("", "")
            L("--- Paths ---", "")
            from app.services.session_service import get_data_dir, get_session_path, get_avatar_cache_dir
            dd = get_data_dir()
            L("Data dir", str(dd))
            L("Data dir exists", str(dd.exists()))
            sp = get_session_path()
            L("Session path", sp)
            av = get_avatar_cache_dir()
            L("Avatars dir", str(av))

            L("", "")
            L("--- Session File ---", "")
            for sfx in (".session", ".session-journal", ".session-wal", ".session-shm"):
                p = sp + sfx
                if os.path.exists(p):
                    sz = os.path.getsize(p)
                    L(f"  {sfx}", f"exists  {sz} B")
                else:
                    L(f"  {sfx}", "not found")

            L("", "")
            L("--- Telegram Client ---", "")
            c = self._client
            L("Connected", str(c.client.is_connected()))
            L("Auth state", c.auth_state.value if c.auth_state else "None")
            L("Has QR login", str(hasattr(c, '_qr_login') and c._qr_login is not None))
            if c.last_error:
                L("Last error", c.last_error)
            L("_authorized", str(c.client._authorized))
            L("auth_key exists", str(bool(c.client.session.auth_key)))
            try:
                L("Active DC", str(c.client.session.dc_id))
            except Exception:
                pass

            L("", "")
            L("--- Worker ---", "")
            L("Running", str(self._worker.isRunning()))
            L("Loop running", str(self._worker.loop.is_running() if self._worker.loop else False))

        except Exception as e:
            import traceback
            L("ERROR", str(e))
            L("TRACEBACK", traceback.format_exc())

        text = "\n".join(lines)
        dlg = QDialog(self)
        dlg.setWindowTitle("Debug Info")
        dlg.resize(640, 520)
        lay = QVBoxLayout(dlg)
        te = QTextEdit()
        te.setReadOnly(True)
        te.setPlainText(text)
        te.setStyleSheet("font-family: monospace; font-size: 12px; background: #1a1a2e; color: #00ff00;")
        lay.addWidget(te)
        bb = QDialogButtonBox(QDialogButtonBox.Ok)
        bb.accepted.connect(dlg.accept)
        lay.addWidget(bb)
        dlg.exec()

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def closeEvent(self, event) -> None:
        """Clean up on window close. Stops the async worker gracefully."""
        # Stop the bulk-leave operation if running
        self._leave_running = False
        self._channel_manager.stop()

        # Stop the async worker's event loop
        if hasattr(self, "_worker") and self._worker.isRunning():
            self._worker.stop()
            # Wait with timeout; if it doesn't finish, move on
            if not self._worker.wait(2000):
                self._worker.terminate()
                self._worker.wait(1000)
        event.accept()
