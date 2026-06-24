"""Authentication widget for TeleClean.

Supports QR-code scan (primary) and phone-number + code (fallback)
authorisation flows.

All async operations are scheduled via a ``run_async`` callable provided
by the parent (typically ``MainWindow._run_async``).  GUI updates happen
on the Qt main thread via signals or direct calls from callbacks.
"""

from __future__ import annotations

import io
import logging
from typing import Any, Awaitable, Callable, Optional

import qrcode
from PIL.Image import Image as PILImage
from PySide6.QtCore import Qt, Signal, Slot, QTimer
from PySide6.QtGui import QPixmap
from PySide6.QtWidgets import (
    QLabel,
    QLineEdit,
    QPushButton,
    QSizePolicy,
    QStackedWidget,
    QVBoxLayout,
    QWidget,
)

from telethon import errors

from app.core.telegram_client import AuthState, TelegramClientWrapper

logger = logging.getLogger(__name__)

QR_REFRESH_INTERVAL_S = 30
QR_POLL_INTERVAL_S = 2


class AuthWidget(QWidget):
    """Main authentication widget.

    Parameters
    ----------
    client : TelegramClientWrapper
    run_async : callable
        ``run_async(coro, on_result=None, on_error=None)`` — schedules a
        coroutine on the background worker and invokes callbacks on the Qt
        main thread with the result/error.
    parent : QWidget or None

    Signals
    -------
    authenticated()
        Emitted once the user has successfully authenticated.
    """

    authenticated = Signal()

    def __init__(
        self,
        client: TelegramClientWrapper,
        run_async: Callable[
            [Awaitable[Any], Optional[Callable], Optional[Callable]], Any
        ],
        parent: Optional[QWidget] = None,
    ) -> None:
        super().__init__(parent)
        self._client = client
        self._run_async = run_async

        # Timer for QR refresh (Qt main-thread timer)
        self._qr_refresh_timer: Optional[QTimer] = None
        # Timer for polling QR scan status
        self._qr_poll_timer: Optional[QTimer] = None

        self._build_ui()

    # ------------------------------------------------------------------
    # UI construction
    # ------------------------------------------------------------------

    def _build_ui(self) -> None:
        self.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)

        outer = QVBoxLayout(self)
        outer.setContentsMargins(40, 40, 40, 40)
        outer.setAlignment(Qt.AlignCenter)

        self._stack = QStackedWidget()
        outer.addWidget(self._stack)

        self._stack.addWidget(self._build_qr_page())    # index 0
        self._stack.addWidget(self._build_phone_page())  # index 1
        self._stack.addWidget(self._build_tfa_page())    # index 2

        self._stack.setCurrentIndex(0)

    def _build_qr_page(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setAlignment(Qt.AlignCenter)
        layout.setSpacing(16)

        title = QLabel("TeleClean")
        title.setObjectName("titleLabel")
        title.setAlignment(Qt.AlignCenter)
        layout.addWidget(title)

        subtitle = QLabel("Отсканируйте QR-код в Telegram")
        subtitle.setObjectName("subtitleLabel")
        subtitle.setAlignment(Qt.AlignCenter)
        layout.addWidget(subtitle)

        self._qr_label = QLabel()
        self._qr_label.setAlignment(Qt.AlignCenter)
        self._qr_label.setMinimumSize(280, 280)
        self._qr_label.setStyleSheet(
            "background-color: #ffffff; border-radius: 12px; padding: 10px;"
        )
        layout.addWidget(self._qr_label)

        self._qr_status = QLabel("Генерация QR-кода...")
        self._qr_status.setObjectName("statusLabel")
        self._qr_status.setAlignment(Qt.AlignCenter)
        layout.addWidget(self._qr_status)

        phone_btn = QPushButton("Войти по номеру телефона")
        phone_btn.setObjectName("linkButton")
        phone_btn.setCursor(Qt.PointingHandCursor)
        phone_btn.clicked.connect(lambda: self._stack.setCurrentIndex(1))
        layout.addWidget(phone_btn, alignment=Qt.AlignCenter)

        self._qr_error = QLabel("")
        self._qr_error.setObjectName("errorLabel")
        self._qr_error.setAlignment(Qt.AlignCenter)
        self._qr_error.setVisible(False)
        layout.addWidget(self._qr_error)

        return page

    def _build_phone_page(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setAlignment(Qt.AlignCenter)
        layout.setSpacing(12)

        title = QLabel("Вход по номеру телефона")
        title.setObjectName("titleLabel")
        title.setAlignment(Qt.AlignCenter)
        layout.addWidget(title)

        phone_label = QLabel("Номер телефона (в формате +79001234567):")
        layout.addWidget(phone_label)

        self._phone_input = QLineEdit()
        self._phone_input.setPlaceholderText("+79001234567")
        self._phone_input.setMaxLength(20)
        layout.addWidget(self._phone_input)

        self._send_code_btn = QPushButton("Отправить код")
        self._send_code_btn.clicked.connect(self._on_send_code)
        layout.addWidget(self._send_code_btn)

        self._code_input = QLineEdit()
        self._code_input.setPlaceholderText("Код из Telegram")
        self._code_input.setMaxLength(10)
        self._code_input.setVisible(False)
        layout.addWidget(self._code_input)

        self._confirm_code_btn = QPushButton("Подтвердить код")
        self._confirm_code_btn.setVisible(False)
        self._confirm_code_btn.clicked.connect(self._on_confirm_code)
        layout.addWidget(self._confirm_code_btn)

        self._phone_status = QLabel("")
        self._phone_status.setObjectName("statusLabel")
        layout.addWidget(self._phone_status)

        back_btn = QPushButton("Назад к QR-коду")
        back_btn.setObjectName("linkButton")
        back_btn.setCursor(Qt.PointingHandCursor)
        back_btn.clicked.connect(lambda: self._stack.setCurrentIndex(0))
        layout.addWidget(back_btn, alignment=Qt.AlignCenter)

        self._phone_error = QLabel("")
        self._phone_error.setObjectName("errorLabel")
        self._phone_error.setVisible(False)
        layout.addWidget(self._phone_error)

        return page

    def _build_tfa_page(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setAlignment(Qt.AlignCenter)
        layout.setSpacing(12)

        title = QLabel("Двухфакторная аутентификация")
        title.setObjectName("titleLabel")
        title.setAlignment(Qt.AlignCenter)
        layout.addWidget(title)

        subtitle = QLabel("Введите пароль двухфакторной аутентификации:")
        subtitle.setObjectName("subtitleLabel")
        subtitle.setAlignment(Qt.AlignCenter)
        layout.addWidget(subtitle)

        self._tfa_input = QLineEdit()
        self._tfa_input.setPlaceholderText("Пароль 2FA")
        self._tfa_input.setEchoMode(QLineEdit.Password)
        layout.addWidget(self._tfa_input)

        self._tfa_btn = QPushButton("Войти")
        self._tfa_btn.clicked.connect(self._on_tfa_submit)
        layout.addWidget(self._tfa_btn)

        self._tfa_error = QLabel("")
        self._tfa_error.setObjectName("errorLabel")
        self._tfa_error.setVisible(False)
        layout.addWidget(self._tfa_error)

        return page

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def _start_qr_flow(self) -> None:
        """Begin QR-code authorisation (must be called from Qt main thread).

        Sets up timers for QR refresh and scan-polling, then generates
        the first QR code via the async worker.
        """
        # Initial QR generation — async part runs on worker, callback
        # handles GUI update on main thread.
        self._generate_qr(on_error=lambda msg: self._qr_status.setText(msg))

        # QTimer for QR refresh (30 s) — fires on Qt main thread
        self._qr_refresh_timer = QTimer(self)
        self._qr_refresh_timer.timeout.connect(self._on_qr_refresh)
        self._qr_refresh_timer.start(QR_REFRESH_INTERVAL_S * 1000)

        # QTimer for polling QR scan — every 2 s on Qt main thread
        self._qr_poll_timer = QTimer(self)
        self._qr_poll_timer.timeout.connect(self._on_poll_qr)
        self._qr_poll_timer.start(QR_POLL_INTERVAL_S * 1000)

    def stop_auth_flow(self) -> None:
        """Stop all auth-related timers (call from main thread)."""
        if self._qr_refresh_timer:
            self._qr_refresh_timer.stop()
        if self._qr_poll_timer:
            self._qr_poll_timer.stop()

    # ------------------------------------------------------------------
    # QR flow
    # ------------------------------------------------------------------

    def _generate_qr(self, on_error=None):
        """Schedule a QR-code generation on the async worker.

        Callback handles GUI updates on the main thread.
        """
        self._run_async(
            self._client.get_qr_token(),
            on_result=lambda token: self._display_qr(token) if token else (
                on_error("Ошибка генерации QR-кода") if on_error else None
            ),
        )

    def _display_qr(self, token: bytes) -> None:
        """Convert token bytes to a QR image and display it (main thread)."""
        qr_img: PILImage = qrcode.make(token, box_size=6)
        buffer = io.BytesIO()
        qr_img.save(buffer, format="PNG")
        buffer.seek(0)

        pixmap = QPixmap()
        pixmap.loadFromData(buffer.getvalue(), "PNG")
        scaled = pixmap.scaled(260, 260, Qt.KeepAspectRatio, Qt.SmoothTransformation)
        self._qr_label.setPixmap(scaled)
        self._qr_status.setText("QR-код готов. Отсканируйте в Telegram.")

    @Slot()
    def _on_qr_refresh(self) -> None:
        """QTimer slot: refresh the QR code image."""
        self._generate_qr(on_error=lambda msg: self._qr_status.setText(msg))

    @Slot()
    def _on_poll_qr(self) -> None:
        """QTimer slot: check whether the QR code was scanned."""
        self._run_async(
            self._client.wait_for_qr_accept(timeout=QR_POLL_INTERVAL_S),
            on_result=lambda success: self._on_qr_accepted() if success else None,
        )

    def _on_qr_accepted(self) -> None:
        """Called when QR scan succeeded."""
        if self._qr_refresh_timer:
            self._qr_refresh_timer.stop()
        if self._qr_poll_timer:
            self._qr_poll_timer.stop()
        self.authenticated.emit()

    # ------------------------------------------------------------------
    # Phone-number flow
    # ------------------------------------------------------------------

    @Slot()
    def _on_send_code(self) -> None:
        phone = self._phone_input.text().strip()
        if not phone:
            self._phone_error.setText("Введите номер телефона")
            self._phone_error.setVisible(True)
            return

        self._phone_error.setVisible(False)
        self._send_code_btn.setEnabled(False)
        self._phone_status.setText("Отправка кода...")

        self._run_async(
            self._client.send_code(phone),
            on_result=lambda success: self._on_code_sent(success),
            on_error=lambda exc: self._on_phone_error(f"Ошибка: {exc}"),
        )

    def _on_code_sent(self, success: bool) -> None:
        if success:
            self._send_code_btn.setVisible(False)
            self._phone_input.setEnabled(False)
            self._code_input.setVisible(True)
            self._confirm_code_btn.setVisible(True)
            self._phone_status.setText("Код отправлен. Проверьте Telegram.")
        else:
            self._send_code_btn.setEnabled(True)
            self._phone_error.setText("Ошибка отправки кода")
            self._phone_error.setVisible(True)
            self._phone_status.setText("")

    @Slot()
    def _on_confirm_code(self) -> None:
        code = self._code_input.text().strip()
        if not code:
            self._phone_error.setText("Введите код из Telegram")
            self._phone_error.setVisible(True)
            return

        self._phone_error.setVisible(False)
        self._confirm_code_btn.setEnabled(False)
        self._phone_status.setText("Проверка кода...")

        self._run_async(
            self._confirm_code_async(code),
            on_error=lambda exc: self._on_phone_error(f"Ошибка: {exc}"),
        )

    async def _confirm_code_async(self, code: str) -> None:
        """Async helper for code confirmation (may raise 2FA)."""
        try:
            success = await self._client.sign_in_with_code(code)
            if success:
                self.authenticated.emit()
            else:
                self._confirm_code_btn.setEnabled(True)
                self._phone_error.setText("Неверный код. Попробуйте снова.")
                self._phone_error.setVisible(True)
                self._phone_status.setText("")
        except errors.SessionPasswordNeededError:
            self._phone_status.setText("Требуется пароль 2FA")
            self._stack.setCurrentIndex(2)
            self._confirm_code_btn.setEnabled(True)

    # ------------------------------------------------------------------
    # 2FA flow
    # ------------------------------------------------------------------

    @Slot()
    def _on_tfa_submit(self) -> None:
        password = self._tfa_input.text()
        if not password:
            self._tfa_error.setText("Введите пароль")
            self._tfa_error.setVisible(True)
            return

        self._tfa_error.setVisible(False)
        self._tfa_btn.setEnabled(False)

        self._run_async(
            self._client.check_2fa_password(password),
            on_result=lambda success: self._on_tfa_result(success),
            on_error=lambda exc: self._on_tfa_error(str(exc)),
        )

    def _on_tfa_result(self, success: bool) -> None:
        if success:
            self.authenticated.emit()
        else:
            self._tfa_btn.setEnabled(True)
            self._tfa_error.setText("Неверный пароль")
            self._tfa_error.setVisible(True)

    def _on_phone_error(self, message: str) -> None:
        self._phone_error.setText(message)
        self._phone_error.setVisible(True)
        self._send_code_btn.setEnabled(True)
        self._confirm_code_btn.setEnabled(True)

    def _on_tfa_error(self, message: str) -> None:
        self._tfa_error.setText(message)
        self._tfa_error.setVisible(True)
        self._tfa_btn.setEnabled(True)
