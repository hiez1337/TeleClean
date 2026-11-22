@echo off
cd /d "%~dp0"
python -m pytest tests/ -v --tb=short
exit /b %ERRORLEVEL%
