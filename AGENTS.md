# TeleClean — WinUI agent guide

This branch contains C# only. Python / PySide6 / Telethon lives on `python`.

## Architecture
.NET 8, WinUI 3 (Windows App SDK 1.5), CommunityToolkit.Mvvm, WTelegramClient 4.4.8.
`src/TeleClean.Core` holds models, Telegram, cache/config and sequential bulk engine.
`src/TeleClean.App` uses singleton view models and DI; UI updates go through IDispatcherService.
`src/TeleClean.Tests` contains offline xUnit business tests. `tests/` contains historical stress harnesses.
Use `long` for peer identifiers. Never mix user/chat/channel identity namespaces.

## Build and test (Windows x64, .NET 8 SDK and Windows SDK)
```
dotnet test src/TeleClean.Tests/TeleClean.Tests.csproj -c Release
dotnet build src/TeleClean.App/TeleClean.App.csproj -c Release -p:Platform=x64
```
Run `src/TeleClean.App/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/TeleClean.App.exe`.
All required NuGet packages restore from nuget.org; no local package directory is required.

## Safety and verification
Preserve user session files and unrelated changes. Never test destructive operations against a real account.
Tests use fake services. Keep automated business tests independent of GUI; manually inspect real WinUI screens in both themes.
Never commit API credentials, session files, personal metadata, caches, logs or build output.
Do not claim account integration or UI verification without actually running it.
CI must test Core and build WinUI on Windows. Upload artifacts; do not automatically publish releases.
