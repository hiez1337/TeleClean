# TeleClean — agent guide

## Quick start
```
pip install -r requirements.txt
python main.py
pytest -v --tb=short
.\build_exe.bat                # produces dist/TeleClean.exe (~78 MB)
```

## Architecture

- **GUI**: PySide6 (Qt 6). Channel list uses `QStyledItemDelegate` (custom `paint()`), not widget-per-row.
  Main window uses `QTabWidget` with 4 tabs: channels, chats, bots, deleted accounts.
- **Telegram API**: Telethon (async). Bridged to Qt thread via `AsyncWorker` (QThread + asyncio event loop).
- **Entrypoint**: `main.py` — loads `.env`, applies dark QSS, creates `QApplication + MainWindow`.

```
main.py → MainWindow → QStackedWidget: [AuthWidget | QTabWidget]
                │   ├── 📺 Каналы (ChannelListWidget, dialog_type="channel")
                │   ├── 👥 Чаты   (ChannelListWidget, dialog_type="supergroup,group")
                │   ├── 🤖 Боты   (ChannelListWidget, dialog_type="bot")
                │   └── 🗑 Удалённые (ChannelListWidget, dialog_type="deleted")
                └── AsyncWorker (QThread) ── TelegramClientWrapper (Telethon)
```

## Dialog types — 6 types
`Channel.dialog_type` is a string field. Constants in `app/models/channel.py`:

| Constant | Value | Telethon entity |
|---|---|---|
| `DIALOG_CHANNEL` | `"channel"` | `Channel.broadcast=True` |
| `DIALOG_SUPERGROUP` | `"supergroup"` | `Channel.megagroup=True` |
| `DIALOG_GROUP` | `"group"` | `Chat` |
| `DIALOG_BOT` | `"bot"` | `User.bot=True` |
| `DIALOG_USER` | `"user"` | `User` (regular) |
| `DIALOG_DELETED` | `"deleted"` | `User.deleted=True` |

## get_all_dialogs() vs get_all_channels()
`get_all_dialogs()` calls `client.get_dialogs(limit=None)` and classifies every entity by type. It replaces the old `get_all_channels()` which only returned broadcast channels. The legacy `get_all_channels()` is kept as a wrapper that filters to `dialog_type="channel"`.

## leave_dialog() dispatcher
`leave_dialog(channel)` routes to the correct Telethon method based on `dialog_type`:
- `channel` / `supergroup` → `LeaveChannelRequest`
- `group` → `DeleteChatUserRequest(chat_id, user_id='self')`
- `user` / `bot` / `deleted` → `client.delete_dialog()`

## API keys — priority
1. `os.getenv("TELEGRAM_API_ID")` — from `.env` (dev) or CI env (test jobs)
2. `load_api_keys()` — saved via **Settings → API ключи Telegram** in the app, stored in `config.json`
3. Hardcoded fallback `_BUILD_API_ID = 11600115` in `app/core/telegram_client.py:24`

## Signal types — 64-bit channel IDs
Telegram channel IDs exceed 32-bit signed int. All signal declarations must use `Signal(object)` or `Signal(object, bool)` — NOT `Signal(int)`.

## QR login gotcha
When the server returns `LoginTokenSuccess` (already authorized; session is still alive despite local reset), `get_qr_token()` returns the string `"LOGIN_SUCCESS"`, not a URL. Upstream callers must handle this: it means *skip QR, go to authenticated state*.

## reset_session() — log_out + new TelegramClient
`reset_session()` calls `client.log_out()` so the server invalidates the session on ALL devices (phone sees "session terminated"). After `log_out()`, Telethon's own docs say *"client is unusable — create a new instance"*. The code creates a brand-new `TelegramClient` with zero stale state. This is why you must NOT reuse the old `TelegramClient` directly.

## SessionPasswordNeededError — handled, not an error
2FA after QR scan raises `SessionPasswordNeededError`. The worker logs it as `DEBUG` (not `ERROR`) when an `on_error` callback exists. The auth widget catches it and switches to the 2FA password page. `refresh_qr_token()` also catches it silently.

## Stale-task cancellation
`_restart_auth()` cancels any in-flight `load_channels` future before calling `reset_session()`. Without this, a flood-waiting `get_dialogs()` would resume on the new `TelegramClient` before `connect()` finishes. `_on_channels_loaded` also checks `auth_state` as a guard.

## Tab distribution
After `get_all_dialogs()` returns, `_distribute_channels()` in MainWindow splits the list by `dialog_type` into 4 `ChannelListWidget` instances — one per tab. Each tab has independent selection, search, and filter state.

## Deleted-account cleanup
The 🗑 Удалённые tab shows dialogs where `dialog_type="deleted"`. The action button changes to "Удалить выбранные" and calls `client.delete_dialog()` instead of `LeaveChannelRequest`. The confirmation dialog and completion message differ from the leave flow.

## Async worker pattern
Use `_run_async(coro, on_result=fn, on_error=fn)` from MainWindow. Both callbacks always run on the Qt main thread. Never call GUI methods from the worker thread directly.

## Session / data dir
- Dev: `<project-root>/.teleclean/`
- Frozen `.exe`: `%APPDATA%/TeleClean/`
- Avatar cache: `<data_dir>/avatars/{channel_id}.jpg`

## Tests
- **Only business logic** (`ChannelManager`, `Channel` model). No GUI tests.
- `conftest.py` provides `FakeTelegramClient` (mock with `leave_dialog`, `delete_dialog`), `sample_channels` (10 fixtures including all 6 dialog types), and `manager` fixture wired to `FakeTelegramClient`.
- All 17 tests run offline, no Telegram connection needed.
- Run: `pytest -v --tb=short`

## PyInstaller build
Excludes ~40 unused Qt modules to keep `.exe` ~78 MB (vs 278 MB with all modules). See `build_exe.bat` or `.github/workflows/ci.yml` for the full exclude list. CI builds automatically on push to `main` and publishes a Release.

## Theme (QSS)
Two files: `telegram_dark.qss` / `telegram_light.qss`, colors from `@xelene/tgui` design tokens. Applies via `QApplication.setStyleSheet()` at startup and on toggle. Saved setting in `config.json`.
