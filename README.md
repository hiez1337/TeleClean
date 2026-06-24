# TeleClean

![Python](https://img.shields.io/badge/python-3.12-blue)
![PySide6](https://img.shields.io/badge/PySide6-6.11-green)
![Telethon](https://img.shields.io/badge/Telethon-1.44-purple)
![CI](https://github.com/hiez1337/TeleClean/actions/workflows/ci.yml/badge.svg)
[![Latest Release](https://img.shields.io/github/v/release/hiez1337/TeleClean)](https://github.com/hiez1337/TeleClean/releases/latest)

**TeleClean** — десктопное приложение для массового выхода из Telegram-каналов, чатов и удаления диалогов с удалёнными аккаунтами.  
Стилизовано под Telegram Desktop. Работает "из коробки" — никаких ключей, .env и Python.

## Скачать

👉 [**Latest Release**](https://github.com/hiez1337/TeleClean/releases/latest) — `TeleClean.exe` (~78 MB)

## Возможности

- ✅ **Авторизация** — QR-код, номер телефона + SMS, 2FA
- ✅ **Список диалогов** — все каналы, супергруппы, чаты, боты, личные диалоги с реальными аватарками
- ✅ **Поиск / фильтр / сортировка** — по названию, активности, подписчикам/участникам
- ✅ **Вкладки** — 📺 Каналы, 👥 Чаты, 🤖 Боты, 🗑 Удалённые аккаунты
- ✅ **Массовый выход** — выберите несколько диалогов, прогресс-бар, кнопка Стоп
- ✅ **Удаление чатов с удалёнными аккаунтами** — находит диалоги с деактивированными пользователями и удаляет их
- ✅ **Сброс сессии** — завершает сессию на всех устройствах (включая телефон)
- ✅ **Темы** — Dark и Light, цвета из официального Telegram UI (tgui)
- ✅ **API-ключи встроены** — `.exe` работает сразу после скачивания
- ✅ **Настройки API** — если ключи не работают, можно ввести свои в интерфейсе
- ✅ **Информация для отладки** — все пути, статус соединения, версии
- ✅ **Автосборка** — CI создаёт релиз при каждом пуше в `main`

## Как использовать

1. Скачайте `TeleClean.exe` из [последнего релиза](https://github.com/hiez1337/TeleClean/releases/latest)
2. Запустите — откроется окно с QR-кодом
3. Отсканируйте QR в Telegram (Настройки → Устройства)
4. После авторизации загрузится список всех диалогов с вкладками:
   - 📺 **Каналы** — broadcast-каналы
   - 👥 **Чаты** — группы и супергруппы
   - 🤖 **Боты** — личные чаты с ботами
   - 🗑 **Удалённые** — чаты с деактивированными аккаунтами
5. Выберите диалоги → нажмите **"Выйти из выбранных"** (или **"Удалить выбранные"** для вкладки удалённых)

### Сброс авторизации

**Файл → Перезапустить авторизацию** — завершает текущую сессию на сервере Telegram (сессия становится неактивной на всех устройствах, включая телефон). После сброса потребуется повторная авторизация.

## Если QR-код не появляется

Откройте **Настройки → API ключи Telegram**, получите свои ключи на [my.telegram.org/apps](https://my.telegram.org/apps) и введите их. Затем перезапустите приложение.

## Диагностика

**Помощь → Информация для отладки** — показывает:
- Режим (Frozen .exe / Dev)
- Источник API-ключей
- Путь к данным, сессии, аватарам
- Состояние Telethon-клиента
- Статус AsyncWorker

## Сборка из исходников

```bash
git clone https://github.com/hiez1337/TeleClean.git
cd TeleClean
pip install -r requirements.txt
python main.py
```

Для сборки `.exe`:

```bash
.\build_exe.bat
```

Готовый файл: `dist\TeleClean.exe`.

## Технологии

| Слой | Технология |
|---|---|
| GUI | PySide6 (Qt 6) |
| Telegram API | Telethon (async MTProto) |
| Темизация | QSS по токенам @xelene/tgui |
| Аватарки | QPixmap + QPainter (круглая обрезка) |
| Сборка | PyInstaller — единый .exe |

## Структура проекта

```
TeleClean/
├── main.py                          # Точка входа
├── AGENTS.md                        # Guide для AI-агентов
├── build_exe.bat                    # PyInstaller скрипт
├── app/
│   ├── core/
│   │   ├── telegram_client.py       # Telethon обёртка, QR, 2FA, диалоги
│   │   └── channel_manager.py       # Бизнес-логика, фильтры, bulk leave
│   ├── models/
│   │   └── channel.py               # Channel dataclass + dialog_type
│   ├── services/
│   │   ├── async_worker.py          # QThread + asyncio bridge
│   │   └── session_service.py       # Config, session, avatars
│   └── gui/
│       ├── main_window.py           # QTabWidget (каналы/чаты/боты/удалённые)
│       ├── auth_widget.py
│       ├── channel_list.py          # QStyledItemDelegate, subtitle per type
│       └── styles/
│           ├── telegram_dark.qss
│           └── telegram_light.qss
├── tests/
│   ├── conftest.py                  # FakeTelegramClient, fixtures
│   ├── test_channel_manager.py
│   └── test_channel_model.py
└── .github/workflows/ci.yml         # Test → Build → Release
```

## Лицензия

MIT
