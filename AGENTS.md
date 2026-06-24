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
- **Telegram API**: Telethon (async). Bridged to Qt thread via `AsyncWorker` (QThread + asyncio event loop).
- **Entrypoint**: `main.py` — loads `.env`, applies dark QSS, creates `QApplication + MainWindow`.

```
main.py → MainWindow → QStackedWidget: [AuthWidget | ChannelListWidget]
                └── AsyncWorker (QThread) ── TelegramClientWrapper (Telethon)
```

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

## Async worker pattern
Use `_run_async(coro, on_result=fn, on_error=fn)` from MainWindow. Both callbacks always run on the Qt main thread. Never call GUI methods from the worker thread directly.

## Session / data dir
- Dev: `<project-root>/.teleclean/`
- Frozen `.exe`: `%APPDATA%/TeleClean/`
- Avatar cache: `<data_dir>/avatars/{channel_id}.jpg`

## Tests
- **Only business logic** (`ChannelManager`, `Channel` model). No GUI tests.
- `conftest.py` provides `FakeTelegramClient` (mock, no real Telethon), `sample_channels` (6 fixtures), and `manager` fixture wired to `FakeTelegramClient`.
- All 16 tests run offline, no Telegram connection needed.
- Run: `pytest -v --tb=short`

## PyInstaller build
Excludes ~40 unused Qt modules to keep `.exe` ~78 MB (vs 278 MB with all modules). See `build_exe.bat` or `.github/workflows/ci.yml` for the full exclude list. CI builds automatically on push to `main` and publishes a Release.

## Theme (QSS)
Two files: `telegram_dark.qss` / `telegram_light.qss`, colors from `@xelene/tgui` design tokens. Applies via `QApplication.setStyleSheet()` at startup and on toggle. Saved setting in `config.json`.
