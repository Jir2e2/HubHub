# HubHub

Интеллектуальный менеджер GitHub-ссылок. Сканирует публичные репозитории,
извлекает ссылки из README.md, .txt, .md файлов, нормализует, удаляет дубли
и агрегирует в MySQL с веб-интерфейсом для поиска и фильтрации.

## Стек

- Python микросервис — сканирование GitHub API, извлечение ссылок (LLM + regex), запись в MySQL
- C# микросервис — REST API, прямые SQL-запросы к MySQL (без ORM)
- Фронтенд — статический HTML/CSS/JS, тёмная тема GitHub UI
- MySQL 8 — реляционная БД
- Docker, Docker Compose — развёртывание на Debian Linux

## Переменные окружения

| Переменная | Описание | Пример |
|---|---|---|
| `GITHUB_TOKEN` | GitHub OAuth токен или PAT (обязательно) | `ghp_xxxx` |
| `MYSQL_ROOT_PASSWORD` | Пароль root MySQL | `rootpass` |
| `MYSQL_DATABASE` | Имя БД | `hubhub` |
| `MYSQL_USER` | Пользователь БД | `hubhub` |
| `MYSQL_PASSWORD` | Пароль пользователя БД | `hubhubpass` |
| `LLM_API_URL` | URL LLM API (опционально) | `http://llm:8000/v1/chat/completions` |
| `LLM_API_KEY` | Ключ LLM API (опционально) | `sk-xxxx` |

> **Важно:** без `GITHUB_TOKEN` сканирование не сработает — сервер вернёт ошибку 400. Получить токен можно на https://github.com/settings/personal-access-tokens/new (достаточно прав `public_repo`).

## Запуск

```bash
cp .env.example .env
# отредактируйте .env и вставьте свой GITHUB_TOKEN
chmod +x build.sh
./build.sh
```

Скрипт соберёт образы, запустит контейнеры, применит миграцию БД и поднимет сервисы.

Фронтенд: `http://localhost:3000`
C# API: `http://localhost:5001`
Python API: `http://localhost:5002`