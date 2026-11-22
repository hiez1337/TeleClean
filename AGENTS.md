# TeleClean · WinUI

Рабочая ветка WinUI содержит самостоятельный проект C#. Python, PySide6, Telethon и PyInstaller относятся к ветке `python`.

## Архитектура

- `src/TeleClean.Core` содержит модели, WTelegramClient 4.4.8, конфигурацию, кэш и последовательный движок массовой обработки.
- `src/TeleClean.App` использует .NET 8, WinUI 3 из Windows App SDK 1.5 и CommunityToolkit.Mvvm. Из фоновых задач обновляйте представления через `IDispatcherService`.
- `src/TeleClean.Tests` содержит штатный набор xUnit с фейковыми сервисами. Проекты в корневом `tests/` служат историческими экспериментами и не участвуют в WinUI CI.

Telegram ID хранится как `long`. Для сопоставления строк учитывайте тип вместе с ID. Для фотографий используйте `DialogItem.AvatarCacheKey`, разделяющий пространства пользователей, групп и каналов. Для дат сообщений сопоставляйте тип и ID собеседника вместе с ID сообщения.

## Сборка и проверка

Из корня checkout на Windows x64 с .NET 8 SDK и Windows SDK выполните:

```powershell
dotnet test src/TeleClean.Tests/TeleClean.Tests.csproj -c Release
dotnet build src/TeleClean.App/TeleClean.App.csproj -c Release -p:Platform=x64
```

Точка запуска: `src/TeleClean.App/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/TeleClean.App.exe`. NuGet восстанавливает пакеты с nuget.org. Windows CI запускает xUnit и публикует архив сборки как артефакт.

## Безопасная проверка UI

Подменяйте `ITelegramService` в тестовой сборке и передавайте `ConfigService` отдельный временный каталог. Проверяйте светлую и тёмную темы, клавиатурный фокус, узкий режим навигации и отмену массовой операции. Описывайте, какие сценарии выполнены на фейковых данных и какие проверены только тестами или по коду.

Сохраняйте пользовательские сессии и сторонние изменения. Исключайте из Git API-реквизиты, данные прокси, файлы сессий, метаданные аккаунтов, кэш, журналы и выходные файлы сборки.
