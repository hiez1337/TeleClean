@echo off
cd /d "%~dp0"
python -m compileall app tests -q
if errorlevel 1 exit /b 1
python -m pytest tests/ -v --tb=short
exit /b %ERRORLEVEL%
