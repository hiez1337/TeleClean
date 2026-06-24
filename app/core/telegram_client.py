"""Asynchronous Telethon wrapper for TeleClean.

Provides QR-code and phone-number authorization, channel listing with
pagination, and bulk-leave operations.
"""

from __future__ import annotations

import asyncio
import logging
import os
from enum import Enum
from typing import Callable, Optional

from telethon import TelegramClient, errors
from telethon.sessions import SQLiteSession
from telethon.tl.functions.channels import LeaveChannelRequest
from telethon.tl.types import Channel

from app.models.channel import Channel as ChannelModel
from app.services.session_service import get_avatar_path, get_session_path

try:
    from app.core.api_keys import API_ID as _EMBEDDED_API_ID, API_HASH as _EMBEDDED_API_HASH
except ImportError:
    _EMBEDDED_API_ID = None
    _EMBEDDED_API_HASH = None

logger = logging.getLogger(__name__)


class AuthState(Enum):
    """Possible states of the authentication flow."""

    NOT_AUTHENTICATED = "not_authenticated"
    WAITING_FOR_QR_SCAN = "waiting_for_qr_scan"
    WAITING_FOR_CODE = "waiting_for_code"
    AUTHENTICATED = "authenticated"
    ERROR = "error"


class LeaveResult(Enum):
    """Result of a single leave-channel operation."""

    SUCCESS = "success"
    RATE_LIMITED = "rate_limited"
    FORBIDDEN = "forbidden"
    NOT_FOUND = "not_found"
    ERROR = "error"


class TelegramClientWrapper:
    """Async wrapper around Telethon's TelegramClient.

    Usage
    -----
    Called from a background thread (QThread) that runs an asyncio event loop.

    API keys: tries .env first, falls back to embedded keys (api_keys.py).
    """

    def __init__(self) -> None:
        api_id_s = os.getenv("TELEGRAM_API_ID")
        api_hash_s = os.getenv("TELEGRAM_API_HASH")

        if api_id_s and api_hash_s:
            self._api_id = int(api_id_s)
            self._api_hash = api_hash_s
        elif _EMBEDDED_API_ID is not None and _EMBEDDED_API_HASH is not None:
            self._api_id = _EMBEDDED_API_ID
            self._api_hash = _EMBEDDED_API_HASH
        else:
            raise RuntimeError(
                "TELEGRAM_API_ID and TELEGRAM_API_HASH must be set in .env"
            )
        self._session_path: str = get_session_path()

        self.client: TelegramClient = TelegramClient(
            self._session_path,
            self._api_id,
            self._api_hash,
            device_model="TeleClean Desktop",
            app_version="1.0.0",
        )

        self._auth_state: AuthState = AuthState.NOT_AUTHENTICATED
        self._last_qr_token: Optional[bytes] = None
        self._phone: Optional[str] = None
        self._phone_code_hash: Optional[str] = None
        self._phone_registered: Optional[bool] = None

    # ------------------------------------------------------------------
    # Auth helpers
    # ------------------------------------------------------------------

    @property
    def auth_state(self) -> AuthState:
        return self._auth_state

    @auth_state.setter
    def auth_state(self, value: AuthState) -> None:
        logger.debug("Auth state: %s -> %s", self._auth_state, value)
        self._auth_state = value

    async def is_user_authorized(self) -> bool:
        """Check whether a valid session already exists."""
        return await self.client.is_user_authorized()

    async def connect(self) -> None:
        """Connect to Telegram servers."""
        await self.client.connect()

    async def disconnect(self) -> None:
        """Disconnect from Telegram servers."""
        await self.client.disconnect()
        self.auth_state = AuthState.NOT_AUTHENTICATED

    async def reset_session(self) -> None:
        """Disconnect and destroy session data for fresh authorization.

        Closes the SQLite session file, deletes it (and journal/WAL files)
        from disk, and creates a fresh empty session.  On the next
        ``start_with_session()`` the client will require QR / phone login
        because no auth data exists.
        """
        # 1. Disconnect from Telegram
        await self.client.disconnect()

        # 2. Close the SQLite session to release the file lock
        try:
            self.client.session.close()
        except Exception as exc:
            logger.warning("Error closing session: %s", exc)

        # 3. Delete the session file AND any SQLite journal/WAL files
        session_base = get_session_path()
        for suffix in (".session", ".session-journal", ".session-wal", ".session-shm"):
            f = session_base + suffix
            try:
                if os.path.exists(f):
                    os.unlink(f)
                    logger.info("Deleted %s", f)
            except PermissionError:
                logger.warning("Permission denied deleting %s", f)
            except Exception as exc:
                logger.error("Failed to delete %s: %s", f, exc)

        # 4. Replace the in-memory session with a fresh empty one so the
        #    same client object can reconnect without SQLite table errors.
        try:
            self.client.session = SQLiteSession(self._session_path)
            logger.info("Created fresh empty SQLiteSession")
        except Exception as exc:
            logger.error("Failed to create fresh session: %s", exc)

        # 5. Force _authorized to False so is_user_authorized() skips the
        #    server round-trip altogether and returns False immediately.
        self.client._authorized = False

        # 6. Reset internal state
        self.auth_state = AuthState.NOT_AUTHENTICATED
        self._qr_login = None
        self._last_qr_token = None

    async def start_with_session(self) -> bool:
        """Try to start the client with an existing session.

        Returns True if already authorised.
        """
        await self.connect()
        authorized = await self.is_user_authorized()
        logger.info(
            "start_with_session: is_user_authorized=%s, _authorized=%s, "
            "auth_key=%s",
            authorized,
            getattr(self.client, "_authorized", "N/A"),
            bool(self.client.session.auth_key),
        )
        if authorized:
            self.auth_state = AuthState.AUTHENTICATED
            return True
        return False

    # ------------------------------------------------------------------
    # QR-code authorisation
    # ------------------------------------------------------------------

    async def get_qr_token(self) -> Optional[str]:
        """Generate a new QR login token.

        Returns the ``tg://login`` URL (string) that should be rendered as
        a QR code, or None on failure.
        """
        try:
            self._qr_login = await self.client.qr_login()
            self._last_qr_token = self._qr_login.token
            self.auth_state = AuthState.WAITING_FOR_QR_SCAN
            return self._qr_login.url
        except Exception as exc:
            logger.error("QR login error: %s", exc)
            self.auth_state = AuthState.ERROR
            return None

    async def refresh_qr_token(self) -> Optional[str]:
        """Refresh (recreate) the QR token after expiry.

        Returns the ``tg://login`` URL for the refreshed QR code, or None
        on failure.  Requires a previous successful ``get_qr_token()`` call.
        """
        if not hasattr(self, '_qr_login') or self._qr_login is None:
            return await self.get_qr_token()
        try:
            await self._qr_login.recreate()
            self._last_qr_token = self._qr_login.token
            self.auth_state = AuthState.WAITING_FOR_QR_SCAN
            return self._qr_login.url
        except Exception as exc:
            logger.error("QR recreate error: %s", exc)
            self.auth_state = AuthState.ERROR
            return None

    async def wait_for_qr_accept(self, timeout: float = 60.0) -> bool:
        """Wait for the user to scan the QR code.

        Uses Telethon's built-in ``QRLogin.wait()`` which **must** be
        running while the QR is displayed for the login to complete.

        Returns True once authenticated.  Returns False on timeout.

        Raises
        ------
        errors.SessionPasswordNeededError
            If the account has 2FA enabled — the caller must prompt for
            the password and complete auth via ``check_2fa_password()``.
        """
        if not hasattr(self, '_qr_login') or self._qr_login is None:
            return False
        try:
            await self._qr_login.wait(timeout=timeout)
            self.auth_state = AuthState.AUTHENTICATED
            return True
        except asyncio.TimeoutError:
            return False
        except errors.SessionPasswordNeededError:
            logger.info("2FA password required after QR scan")
            self.auth_state = AuthState.WAITING_FOR_CODE
            raise
        except Exception as exc:
            logger.error("QR wait error: %s", exc)
            self.auth_state = AuthState.ERROR
            return False

    # ------------------------------------------------------------------
    # Phone-number authorisation
    # ------------------------------------------------------------------

    async def send_code(self, phone: str) -> bool:
        """Request a verification code be sent to *phone*.

        Returns True when the code was sent successfully.
        """
        self._phone = phone
        try:
            result = await self.client.send_code_request(phone)
            self._phone_code_hash = result.phone_code_hash
            self._phone_registered = result.phone_registered
            self.auth_state = AuthState.WAITING_FOR_CODE
            return True
        except errors.PhoneNumberInvalidError:
            logger.warning("Invalid phone number: %s", phone)
            self.auth_state = AuthState.ERROR
            return False
        except errors.PhoneNumberFloodError:
            logger.warning("Flood on phone number: %s", phone)
            self.auth_state = AuthState.ERROR
            return False
        except Exception as exc:
            logger.error("send_code error: %s", exc)
            self.auth_state = AuthState.ERROR
            return False

    async def sign_in_with_code(self, code: str) -> bool:
        """Complete phone-number authorisation with the received *code*.

        Returns True on success.
        """
        if not self._phone:
            self.auth_state = AuthState.ERROR
            return False

        try:
            await self.client.sign_in(
                phone=self._phone,
                code=code,
                phone_code_hash=self._phone_code_hash,
            )
            self.auth_state = AuthState.AUTHENTICATED
            return True
        except errors.SessionPasswordNeededError:
            # Two-factor authentication is enabled — prompt for the password
            logger.info("2FA password required")
            self.auth_state = AuthState.WAITING_FOR_CODE
            # We re-raise a specific marker so the GUI can ask for the password
            raise
        except errors.PhoneCodeInvalidError:
            logger.warning("Invalid code for %s", self._phone)
            self.auth_state = AuthState.ERROR
            return False
        except errors.PhoneCodeExpiredError:
            logger.warning("Code expired for %s", self._phone)
            self.auth_state = AuthState.ERROR
            return False
        except Exception as exc:
            logger.error("sign_in error: %s", exc)
            self.auth_state = AuthState.ERROR
            return False

    async def check_2fa_password(self, password: str) -> bool:
        """Complete 2FA password entry."""
        try:
            await self.client.sign_in(password=password)
            self.auth_state = AuthState.AUTHENTICATED
            return True
        except errors.PasswordHashInvalidError:
            logger.warning("Invalid 2FA password")
            return False
        except Exception as exc:
            logger.error("2FA error: %s", exc)
            self.auth_state = AuthState.ERROR
            return False

    # ------------------------------------------------------------------
    # Channel listing
    # ------------------------------------------------------------------

    async def _download_avatar(self, entity: Channel, semaphore: asyncio.Semaphore) -> None:
        """Download a channel's profile photo to the local cache."""
        avatar_path = get_avatar_path(entity.id)
        if avatar_path.exists():
            return
        async with semaphore:
            try:
                await self.client.download_profile_photo(
                    entity, file=str(avatar_path)
                )
            except Exception as exc:
                logger.debug("No avatar for channel %d: %s", entity.id, exc)

    async def get_all_channels(
        self,
        on_progress: Optional[Callable[[int, int], None]] = None,
    ) -> list[ChannelModel]:
        """Fetch every channel the user is a member of.

        Uses Telethon's native ``get_dialogs(limit=None)`` which handles
        pagination internally, returning all dialogs.  Filters to broadcast
        channels only and downloads their avatars in the background.

        Parameters
        ----------
        on_progress : callable or None
            Called as ``on_progress(current, total)`` during processing.
        """
        dialogs = await self.client.get_dialogs(limit=None)
        total = len(dialogs)

        if on_progress:
            on_progress(0, total)

        channels: list[ChannelModel] = []
        avatar_tasks: list = []
        sem = asyncio.Semaphore(5)

        for i, dialog in enumerate(dialogs):
            if on_progress:
                on_progress(i + 1, total)
            entity = dialog.entity
            if isinstance(entity, Channel) and entity.broadcast:
                channels.append(
                    ChannelModel(
                        id=entity.id,
                        title=dialog.name or "Unknown",
                        username=entity.username,
                        participant_count=getattr(
                            entity, "participants_count", 0
                        ),
                        unread_count=dialog.unread_count,
                        is_channel=True,
                        is_joined=True,
                    )
                )
                avatar_tasks.append(
                    self._download_avatar(entity, sem)
                )

        if avatar_tasks:
            asyncio.ensure_future(
                asyncio.gather(*avatar_tasks, return_exceptions=True)
            )

        return channels

    # ------------------------------------------------------------------
    # Leaving channels
    # ------------------------------------------------------------------

    async def leave_channel(
        self,
        channel_id: int,
    ) -> LeaveResult:
        """Leave a single channel by its Telegram ID.

        Returns a ``LeaveResult`` indicating the outcome.
        """
        try:
            await self.client(LeaveChannelRequest(channel_id))
            logger.info("Left channel %d", channel_id)
            return LeaveResult.SUCCESS
        except errors.FloodWaitError as exc:
            logger.warning(
                "Rate limited on channel %d, wait %ds", channel_id, exc.seconds
            )
            return LeaveResult.RATE_LIMITED
        except errors.ChannelPrivateError:
            logger.warning("Cannot leave private channel %d", channel_id)
            return LeaveResult.FORBIDDEN
        except errors.ChannelInvalidError:
            logger.warning("Invalid channel %d", channel_id)
            return LeaveResult.NOT_FOUND
        except Exception as exc:
            logger.error("Failed to leave channel %d: %s", channel_id, exc)
            return LeaveResult.ERROR
