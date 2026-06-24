# TeleClean

![Python](https://img.shields.io/badge/python-3.10%2B-blue)
![PySide6](https://img.shields.io/badge/PySide6-6.5%2B-green)
![Telethon](https://img.shields.io/badge/Telethon-1.34%2B-purple)
![License](https://img.shields.io/badge/license-MIT-yellow)

**TeleClean** — десктопное приложение для массового выхода из Telegram-каналов с современным интерфейсом, стилизованным под Telegram Desktop.

## Возможности

- **Авторизация** — QR-код (основной) или номер телефона + код из SMS (запасной), поддержка 2FA
- **Загрузка каналов** — получает список всех каналов, где вы состоите + аватарки
- **Поиск и фильтрация** — по названию, непрочитанным, подписчикам
- **Сортировка** — по названию, активности, подписчикам
- **Массовый выход** — выбор нескольких каналов, прогресс-бар, лог, кнопка Стоп
- **Dark / Light темы** — точные цвета из официального Telegram UI дизайна (tgui)
- **Круглые аватарки** — реальные фото каналов из Telegram, кешируются локально

## Скриншоты

*(добавьте скриншот приложения)*

## Быстрый старт

```bash
git clone https://github.com/hiez1337/TeleClean.git
cd TeleClean
pip install -r requirements.txt
python main.py
```

При первом запуске — отсканируйте QR-код в Telegram (Настройки → Устройства → Привязать устройство).

## Сборка .exe

```bash
.\build_exe.bat
```

Готовый файл: `dist\TeleClean.exe` (78 MB). Пользователю не нужен Python или `.env` — ключи API встроены.

## Технологии

| Слой | Технология |
|---|---|
| GUI | PySide6 (Qt 6) |
| Telegram API | Telethon (async MTProto) |
| Темизация | QSS (Qt Style Sheets) по токенам @xelene/tgui |
| Аватарки | QPixmap + QPainter (круглая обрезка) |
| Сборка | PyInstaller — единый .exe |

## Структура проекта

```
TeleClean/
├── main.py                     # Точка входа
├── app/
│   ├── core/
│   │   ├── telegram_client.py  # Telethon обёртка (auth, каналы, выход)
│   │   ├── channel_manager.py  # Бизнес-логика, кеширование, bulk-leave
│   │   └── api_keys.py         # API-ключи (только для сборки, в .gitignore)
│   ├── models/
│   │   └── channel.py          # Data class канала
│   ├── services/
│   │   ├── async_worker.py     # QThread + asyncio bridge
│   │   └── session_service.py  # Персистентность (config, session, avatar cache)
│   └── gui/
│       ├── main_window.py      # Главное окно, bulk-leave workflow
│       ├── auth_widget.py      # Авторизация (QR, phone, 2FA)
│       ├── channel_list.py     # Список каналов (QStyledItemDelegate)
│       └── styles/
│           ├── telegram_dark.qss
│           └── telegram_light.qss
├── tests/
│   ├── test_channel_model.py
│   ├── test_channel_manager.py
│   └── conftest.py
└── build_exe.bat               # Сборка .exe через PyInstaller
```

## Лицензия

MIT
