# TeleClean · WinUI

Нативная Windows-версия: C# / .NET 8 / WinUI 3 / WTelegramClient.
Python-реализация находится в [ветке python](https://github.com/hiez1337/TeleClean/tree/python).

## Сборка
Windows 10 19041+ / Windows 11, .NET 8 SDK и Windows SDK.
```powershell
dotnet test src/TeleClean.Tests/TeleClean.Tests.csproj -c Release
dotnet build src/TeleClean.App/TeleClean.App.csproj -c Release -p:Platform=x64
.\src\TeleClean.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\TeleClean.App.exe
```
Отдельный checkout содержит все исходники и восстанавливает зависимости из nuget.org.
Сессия WinUI несовместима с Telethon; файлы Python не переносите.
