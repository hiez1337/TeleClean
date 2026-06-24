"""Asynchronous Telethon wrapper for TeleClean.

Provides QR-code and phone-number authorization, channel listing with
pagination, and bulk-leave operations.
"""

from __future__ import annotations

import asyncio
import logging
import os
import time
from enum import Enum
from typing import Optional

from dotenv import load_dotenv
from telethon import TelegramClient, errors
from telethon.tl.functions.channels import LeaveChannelRequest
from telethon.tl.types import Channel

from app.models.channel import Channel as ChannelModel
from app.services.session_service import get_session_path

logger = logging.getLogger(__name__)

# Load environment variables once at import time
load_dotenv()


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
    """

    def __init__(self) -> None:
        api_id: Optional[str] = os.getenv("TELEGRAM_API_ID")
        api_hash: Optional[str] = os.getenv("TELEGRAM_API_HASH")

        if not api_id or not api_hash:
            raise RuntimeError(
                "TELEGRAM_API_ID and TELEGRAM_API_HASH must be set in .env"
            )

        self._api_id: int = int(api_id)
        self._api_hash: str = api_hash
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

    async def start_with_session(self) -> bool:
        """Try to start the client with an existing session.

        Returns True if already authorised.
        """
        await self.connect()
        if await self.is_user_authorized():
            self.auth_state = AuthState.AUTHENTICATED
            return True
        return False

    # ------------------------------------------------------------------
    # QR-code authorisation
    # ------------------------------------------------------------------

    async def get_qr_token(self) -> Optional[bytes]:
        """Generate a new QR login token.

        Returns ``bytes`` that should be rendered as a QR code, or None
        on failure.
        """
        try:
            result = await self.client.qr_login()
            self._last_qr_token = result.token
            self.auth_state = AuthState.WAITING_FOR_QR_SCAN
            return result.token
        except Exception as exc:
            logger.error("QR login error: %s", exc)
            self.auth_state = AuthState.ERROR
            return None

    async def wait_for_qr_accept(self, timeout: float = 60.0) -> bool:
        """Wait for the user to scan the QR code.

        Returns True once the QR code has been scanned and the user is
        authenticated.  Returns False if *timeout* seconds elapse.
        """
        if self._last_qr_token is None:
            return False

        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if await self.is_user_authorized():
                self.auth_state = AuthState.AUTHENTICATED
                return True
            await asyncio.sleep(1.0)
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

    async def get_dialogs(self, offset_date=None, limit: int = 50):
        """Fetch dialogs (used internally).

        Returns raw Telethon Dialog objects.
        """
        return await self.client.get_dialogs(
            offset_date=offset_date,
            limit=limit,
        )

    async def get_channels(
        self,
        offset: int = 0,
        limit: int = 50,
        on_progress: Optional[Callable[[int, int], None]] = None,
    ) -> list[ChannelModel]:
        """Retrieve a page of channels the user is a member of.

        Parameters
        ----------
        offset : int
            Number of dialogs to skip.
        limit : int
            Maximum number of dialogs to fetch.
        on_progress : callable or None
            Called as ``on_progress(current, total)`` during fetching.

        Returns
        -------
        list[ChannelModel]
            Channel objects, filtered to actual channels (not users/groups).
        """
        dialogs = await self.client.get_dialogs(limit=offset + limit)
        if on_progress:
            on_progress(0, len(dialogs))

        channels: list[ChannelModel] = []
        for i, dialog in enumerate(dialogs):
            if on_progress:
                on_progress(i + 1, len(dialogs))
            entity = dialog.entity
            if isinstance(entity, Channel) and entity.broadcast:
                # This is a channel (broadcast = True for channels)
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

        # Apply offset/slicing
        return channels[offset:offset + limit]

    async def get_all_channels(
        self,
        on_progress: Optional[Callable[[int, int], None]] = None,
    ) -> list[ChannelModel]:
        """Fetch every channel the user is a member of.

        Iterates through all dialogs with pagination.
        """
        all_channels: list[ChannelModel] = []
        offset = 0
        page_size = 50

        while True:
            page = await self.get_channels(
                offset=offset, limit=page_size, on_progress=on_progress
            )
            if not page:
                break
            all_channels.extend(page)
            offset += page_size
            if len(page) < page_size:
                break

        return all_channels

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
