@echo off
echo ===== TeleClean Verification =====
echo.
echo 1. Checking imports...
cd /d D:\Telegram Vihoditel
python -c "
import sys; sys.path.insert(0, '.')
from app.models.channel import Channel; print('   [OK] Channel model')
from app.services.session_service import get_data_dir; print('   [OK] Session service')
from app.core.telegram_client import AuthState, LeaveResult, TelegramClientWrapper; print('   [OK] Telegram client wrapper')
from app.core.channel_manager import ChannelManager, LeaveProgressCallback; print('   [OK] Channel manager')
from app.gui.auth_widget import AuthWidget; print('   [OK] Auth widget')
from app.gui.channel_list import ChannelListWidget; print('   [OK] Channel list widget')
from app.gui.main_window import MainWindow; print('   [OK] Main window')
from app.services.async_worker import AsyncWorker; print('   [OK] Async worker')
print()
print('   All imports: OK')
" 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo   IMPORTS FAILED ^(see above^)
    exit /b 1
)

echo.
echo 2. Checking syntax...
python -m compileall app/ tests/ -q 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   Syntax: OK
) else (
    echo   Syntax ERRORS found
    exit /b 1
)

echo.
echo 3. Running tests...
python -m pytest tests/ -v --tb=short
if %ERRORLEVEL% EQU 0 (
    echo.
    echo ===== ALL CHECKS PASSED =====
) else (
    echo.
    echo ===== SOME TESTS FAILED =====
    exit /b 1
)
pause
