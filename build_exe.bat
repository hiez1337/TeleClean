@echo off
title TeleClean build

if exist dist rmdir /s /q dist >nul 2>&1
if exist build rmdir /s /q build >nul 2>&1

echo Building TeleClean.exe...

pyinstaller ^
    --onefile ^
    --windowed ^
    --name "TeleClean" ^
    --add-data "app\gui\styles\telegram_dark.qss;app\gui\styles" ^
    --add-data "app\gui\styles\telegram_light.qss;app\gui\styles" ^
    --hidden-import "telethon" ^
    --hidden-import "qrcode" ^
    --hidden-import "PIL" ^
    --hidden-import "app.core.api_keys" ^
    --hidden-import "PIL._imaging" ^
    --hidden-import "dotenv" ^
    --collect-all "telethon" ^
    --exclude-module "PySide6.Qt3DAnimation" ^
    --exclude-module "PySide6.Qt3DCore" ^
    --exclude-module "PySide6.Qt3DExtras" ^
    --exclude-module "PySide6.Qt3DInput" ^
    --exclude-module "PySide6.Qt3DLogic" ^
    --exclude-module "PySide6.Qt3DRender" ^
    --exclude-module "PySide6.QtBluetooth" ^
    --exclude-module "PySide6.QtCharts" ^
    --exclude-module "PySide6.QtDataVisualization" ^
    --exclude-module "PySide6.QtDesigner" ^
    --exclude-module "PySide6.QtGraphs" ^
    --exclude-module "PySide6.QtGraphsWidgets" ^
    --exclude-module "PySide6.QtHelp" ^
    --exclude-module "PySide6.QtHttpServer" ^
    --exclude-module "PySide6.QtLocation" ^
    --exclude-module "PySide6.QtMultimedia" ^
    --exclude-module "PySide6.QtMultimediaWidgets" ^
    --exclude-module "PySide6.QtNetworkAuth" ^
    --exclude-module "PySide6.QtNfc" ^
    --exclude-module "PySide6.QtOpenGL" ^
    --exclude-module "PySide6.QtOpenGLWidgets" ^
    --exclude-module "PySide6.QtPdf" ^
    --exclude-module "PySide6.QtPdfWidgets" ^
    --exclude-module "PySide6.QtPrintSupport" ^
    --exclude-module "PySide6.QtQml" ^
    --exclude-module "PySide6.QtQuick" ^
    --exclude-module "PySide6.QtQuick3D" ^
    --exclude-module "PySide6.QtQuickControls2" ^
    --exclude-module "PySide6.QtQuickWidgets" ^
    --exclude-module "PySide6.QtRemoteObjects" ^
    --exclude-module "PySide6.QtScxml" ^
    --exclude-module "PySide6.QtSensors" ^
    --exclude-module "PySide6.QtSerialBus" ^
    --exclude-module "PySide6.QtSerialPort" ^
    --exclude-module "PySide6.QtSpatialAudio" ^
    --exclude-module "PySide6.QtSql" ^
    --exclude-module "PySide6.QtStateMachine" ^
    --exclude-module "PySide6.QtSvg" ^
    --exclude-module "PySide6.QtSvgWidgets" ^
    --exclude-module "PySide6.QtTest" ^
    --exclude-module "PySide6.QtTextToSpeech" ^
    --exclude-module "PySide6.QtUiTools" ^
    --exclude-module "PySide6.QtWebChannel" ^
    --exclude-module "PySide6.QtWebEngineCore" ^
    --exclude-module "PySide6.QtWebEngineQuick" ^
    --exclude-module "PySide6.QtWebEngineWidgets" ^
    --exclude-module "PySide6.QtWebSockets" ^
    --exclude-module "PySide6.QtWebView" ^
    --exclude-module "PySide6.QtXml" ^
    --exclude-module "PySide6.QtConcurrent" ^
    --exclude-module "PySide6.QtDBus" ^
    --exclude-module "qml" ^
    --exclude-module "PySide6.QtQml" ^
    --noconfirm ^
    main.py

if %errorlevel% neq 0 (
    echo FAILED
    pause
    exit /b 1
)

echo Done: dist\TeleClean.exe
echo.
dir dist\TeleClean.exe /-c | find "TeleClean.exe"
pause
