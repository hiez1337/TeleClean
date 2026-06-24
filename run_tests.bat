@echo off
cd /d D:\Telegram Vihoditel
python -m pytest tests/ -v --tb=short
pause
