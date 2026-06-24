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
from telethon.tl.functions.messages import DeleteChatUserRequest
from telethon.tl.types import Channel, Chat, User

from app.models.channel import Channel as ChannelModel
from app.models.channel import (
    DIALOG_BOT,
    DIALOG_CHANNEL,
    DIALOG_DELETED,
    DIALOG_GROUP,
    DIALOG_SUPERGROUP,
    DIALOG_USER,
)
from app.services.session_service import get_avatar_path, get_session_path, load_api_keys

# Built-in fallback keys (committed to repo, overwritten by CI at build time)
_BUILD_API_ID = 11600115
_BUILD_API_HASH = "dabf2aadd76c3a56983809d54c0760c6"

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

    API keys: tries .env first, falls back to embedded keys (keys.json).
    """

    last_error: str = ""

    def __init__(self) -> None:
        api_id_s = os.getenv("TELEGRAM_API_ID")
        api_hash_s = os.getenv("TELEGRAM_API_HASH")
        api_id = None
        api_hash = None

        # 1. Environment (.env)
        if api_id_s and api_hash_s:
            api_id = int(api_id_s)
            api_hash = api_hash_s
        # 2. Saved config (Settings dialog)
        saved = load_api_keys()
        if not api_id and saved.get("api_id"):
            api_id = saved["api_id"]
            api_hash = saved["api_hash"]
        # 3. Built-in fallback
        if not api_id:
            api_id = _BUILD_API_ID
            api_hash = _BUILD_API_HASH

        self._api_id = api_id
        self._api_hash = api_hash or ""
        self._session_path: str = get_session_path()

        self.client: TelegramClient = TelegramClient(
            self._session_path,
            self._api_id,
            self._api_hash,
            device_model="TeleClean Desktop",
            app_version="1.0.0",
            receive_updates=True,
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
        logger.debug("Connecting to Telegram...")
        await self.client.connect()
        logger.debug("Connected to %s", self.client.session.dc_id if self.client.session else "?")

    async def disconnect(self) -> None:
        """Disconnect from Telegram servers."""
        logger.debug("Disconnecting from Telegram...")
        await self.client.disconnect()
        self.auth_state = AuthState.NOT_AUTHENTICATED
        logger.debug("Disconnected")

    async def reset_session(self) -> None:
        """Log out from Telegram server and fully reinitialize the client.

        1. Calls ``client.log_out()`` — server invalidates the auth key and
           the user sees "Terminate this session" on all other clients.
        2. Deletes stale session files from disk (WAL/journal may remain).
        3. Creates a **completely new** ``TelegramClient`` instance — the old
           one is never reused.  Telethon docs: *"client is unusable after
           logging out and a new instance should be created."*

        A fresh ``TelegramClient`` has zero stale state: no memory of old
        channel subscriptions, no corrupted message box, no lingering
        update-loop tasks.
        """
        # 1. Tell the server to invalidate this session
        try:
            if self.client.is_connected() and self.client.session and self.client.session.auth_key:
                await self.client.log_out()
                logger.info("log_out() — server invalidated the session")
            else:
                await self.client.disconnect()
                logger.info("disconnected (no valid auth key for log_out)")
        except Exception as exc:
            logger.warning("log_out failed: %s", exc)
            try:
                await self.client.disconnect()
            except Exception:
                pass

        # 2. Nuke any leftover session files (log_out deletes .session,
        #    but WAL/journal may survive on disk)
        session_base = get_session_path()
        for suffix in (".session", ".session-journal", ".session-wal", ".session-shm"):
            f = session_base + suffix
            for _ in range(3):
                try:
                    if os.path.exists(f):
                        os.unlink(f)
                        logger.info("Deleted %s", f)
                    break
                except PermissionError:
                    await asyncio.sleep(0.1)
                except Exception as exc:
                    logger.warning("Failed to delete %s: %s", f, exc)
                    break

        # 3. Create brand-new TelegramClient (zero stale state)
        self.client = TelegramClient(
            self._session_path,
            self._api_id,
            self._api_hash,
            device_model="TeleClean Desktop",
            app_version="1.0.0",
            receive_updates=True,
        )
        logger.info("Created brand-new TelegramClient instance")

        self.client._authorized = False
        self.auth_state = AuthState.NOT_AUTHENTICATED
        self._qr_login = None
        self._last_qr_token = None
        self.last_error = ""

    async def start_with_session(self) -> bool:
        """Try to start the client with an existing session.

        Returns True if already authorised.
        """
        await self.connect()
        try:
            authorized = await self.is_user_authorized()
        except errors.AuthRestartError:
            logger.warning("Auth key invalid, resetting session")
            await self.reset_session()
            await self.connect()
            authorized = False
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
        logger.debug("Generating QR login token...")
        for attempt in range(2):
            try:
                if not self.client.is_connected():
                    logger.debug("Not connected, connecting...")
                    await self.client.connect()
                self._qr_login = await self.client.qr_login()
                self._last_qr_token = self._qr_login.url
                self.auth_state = AuthState.WAITING_FOR_QR_SCAN
                self.last_error = ""
                logger.debug("QR token generated successfully")
                return self._qr_login.url
            except errors.AuthRestartError as exc:
                self.last_error = f"AuthRestartError (attempt {attempt+1}): {exc}"
                logger.warning(self.last_error)
                await self.reset_session()
            except errors.SessionPasswordNeededError:
                self.last_error = "2FA required"
                self.auth_state = AuthState.WAITING_FOR_CODE
                raise
            except AttributeError as exc:
                self.last_error = f"LoginTokenSuccess (already authorized): {exc}"
                logger.warning(self.last_error)
                self.auth_state = AuthState.AUTHENTICATED
                self.last_error = ""
                return "LOGIN_SUCCESS"
            except Exception as exc:
                self.last_error = f"{type(exc).__name__}: {exc}"
                logger.error("QR login error: %s", self.last_error)
                break
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
            logger.debug("QR token recreated successfully")
            return self._qr_login.url
        except errors.SessionPasswordNeededError:
            logger.info("2FA required (refresh skipped — handled by wait)")
            return None
        except errors.AuthRestartError:
            logger.warning("AuthRestartError during QR recreate — resetting")
            await self.reset_session()
            return None
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
        logger.debug("Submitting 2FA password...")
        try:
            await self.client.sign_in(password=password)
            self.auth_state = AuthState.AUTHENTICATED
            logger.info("2FA password accepted — authenticated")
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

    async def _download_avatar(self, entity, semaphore: asyncio.Semaphore) -> None:
        """Download a dialog's profile photo to the local cache."""
        avatar_path = get_avatar_path(entity.id)
        if avatar_path.exists():
            return
        async with semaphore:
            try:
                await self.client.download_profile_photo(
                    entity, file=str(avatar_path)
                )
                # Validate JPEG integrity — delete corrupt files
                if avatar_path.stat().st_size > 0:
                    try:
                        from PIL import Image as PILImage
                        with PILImage.open(avatar_path) as img:
                            img.verify()
                    except Exception:
                        logger.warning("Corrupt avatar for dialog %d, deleting", entity.id)
                        avatar_path.unlink(missing_ok=True)
            except Exception as exc:
                logger.debug("No avatar for dialog %d: %s", entity.id, exc)

    async def get_all_dialogs(
        self,
        on_progress: Optional[Callable[[int, int], None]] = None,
    ) -> list[ChannelModel]:
        """Fetch every dialog the user has.

        Uses Telethon's native ``get_dialogs(limit=None)`` which handles
        pagination internally, returning all dialogs.  Detects channel,
        supergroup, group, bot, user, and deleted-account dialogs.
        Downloads avatars in the background.
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
            channel = None

            if isinstance(entity, Channel) and entity.broadcast:
                channel = ChannelModel(
                    id=entity.id,
                    title=dialog.name or "Unknown",
                    username=entity.username,
                    participant_count=getattr(entity, "participants_count", 0),
                    unread_count=dialog.unread_count,
                    is_channel=True,
                    is_joined=True,
                    dialog_type=DIALOG_CHANNEL,
                )
            elif isinstance(entity, Channel) and entity.megagroup:
                channel = ChannelModel(
                    id=entity.id,
                    title=dialog.name or "Unknown",
                    username=entity.username,
                    participant_count=getattr(entity, "participants_count", 0),
                    unread_count=dialog.unread_count,
                    is_channel=False,
                    is_joined=True,
                    dialog_type=DIALOG_SUPERGROUP,
                )
            elif isinstance(entity, Chat):
                channel = ChannelModel(
                    id=entity.id,
                    title=dialog.name or "Unknown",
                    username=None,
                    participant_count=getattr(entity, "participants_count", 0),
                    unread_count=dialog.unread_count,
                    is_channel=False,
                    is_joined=True,
                    dialog_type=DIALOG_GROUP,
                )
            elif isinstance(entity, User) and entity.bot:
                channel = ChannelModel(
                    id=entity.id,
                    title=dialog.name or "Unknown",
                    username=entity.username,
                    participant_count=0,
                    unread_count=dialog.unread_count,
                    is_channel=False,
                    is_joined=True,
                    dialog_type=DIALOG_BOT,
                )
            elif isinstance(entity, User) and entity.deleted:
                channel = ChannelModel(
                    id=entity.id,
                    title=dialog.name or "Unknown",
                    username=None,
                    participant_count=0,
                    unread_count=dialog.unread_count,
                    is_channel=False,
                    is_joined=False,
                    dialog_type=DIALOG_DELETED,
                )
            elif isinstance(entity, User):
                channel = ChannelModel(
                    id=entity.id,
                    title=dialog.name or "Unknown",
                    username=entity.username,
                    participant_count=0,
                    unread_count=dialog.unread_count,
                    is_channel=False,
                    is_joined=True,
                    dialog_type=DIALOG_USER,
                )

            if channel is not None:
                channels.append(channel)
                # Only download avatars for channels, groups, and bots
                if channel.dialog_type in (DIALOG_CHANNEL, DIALOG_SUPERGROUP, DIALOG_GROUP, DIALOG_BOT):
                    avatar_tasks.append(
                        self._download_avatar(entity, sem)
                    )

        if avatar_tasks:
            asyncio.ensure_future(
                asyncio.gather(*avatar_tasks, return_exceptions=True)
            )

        return channels

    async def get_all_channels(
        self,
        on_progress: Optional[Callable[[int, int], None]] = None,
    ) -> list[ChannelModel]:
        """Fetch every broadcast channel the user is a member of (legacy)."""
        all_dialogs = await self.get_all_dialogs(on_progress=on_progress)
        return [d for d in all_dialogs if d.dialog_type == DIALOG_CHANNEL]

    # ------------------------------------------------------------------
    # Leaving channels / groups / dialogs
    # ------------------------------------------------------------------

    async def leave_dialog(self, channel: ChannelModel) -> LeaveResult:
        """Leave or delete a dialog based on its type.

        - Channels / supergroups: LeaveChannelRequest
        - Basic groups: DeleteChatUserRequest
        - Users / bots / deleted: delete_dialog
        """
        dt = channel.dialog_type
        if dt in (DIALOG_CHANNEL, DIALOG_SUPERGROUP):
            return await self.leave_channel(channel.id)
        elif dt == DIALOG_GROUP:
            return await self.leave_group(channel.id)
        elif dt in (DIALOG_USER, DIALOG_BOT, DIALOG_DELETED):
            return await self.delete_dialog(channel.id)
        return LeaveResult.ERROR

    async def leave_channel(
        self,
        channel_id: int,
    ) -> LeaveResult:
        """Leave a channel or supergroup by its Telegram ID.

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

    async def leave_group(self, chat_id: int) -> LeaveResult:
        """Leave a basic group (Chat) by its ID."""
        try:
            await self.client(DeleteChatUserRequest(chat_id=chat_id, user_id="self"))
            logger.info("Left group %d", chat_id)
            return LeaveResult.SUCCESS
        except errors.FloodWaitError as exc:
            logger.warning("Rate limited on group %d, wait %ds", chat_id, exc.seconds)
            return LeaveResult.RATE_LIMITED
        except Exception as exc:
            logger.error("Failed to leave group %d: %s", chat_id, exc)
            return LeaveResult.ERROR

    async def delete_dialog(self, dialog_id: int) -> LeaveResult:
        """Delete a personal dialog (user, bot, or deleted account)."""
        try:
            await self.client.delete_dialog(dialog_id)
            logger.info("Deleted dialog %d", dialog_id)
            return LeaveResult.SUCCESS
        except Exception as exc:
            logger.error("Failed to delete dialog %d: %s", dialog_id, exc)
            return LeaveResult.ERROR
