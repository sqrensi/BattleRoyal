# Деплой на один VPS (PostgreSQL + QueueService)

Один VPS = **PostgreSQL + Node QueueService** (матчмейкинг, профили, realtime). Unity-клиент подключается к **публичному IP** VPS.

Вариант **A** — профили с нуля, без переноса данных из SQLite.

---

## 0. Что понадобится

| Компонент | Минимум | Рекомендация |
|-----------|---------|--------------|
| VPS | 2 vCPU, 2 GB RAM | 4 vCPU, 4–8 GB RAM |
| ОС | Ubuntu 22.04 / 24.04 | |
| Домен | не обязателен | удобнее, чем IP |
| Старые данные SQLite | не нужны | вариант A — с нуля |

**Открытые порты наружу:**

| Порт | Зачем |
|------|--------|
| **5050** | HTTP API (очередь, профили, магазин) |
| **5051** | WebSocket (игра: позы, стрельба, BR) |
| **5432** | Postgres — **только localhost**, не открывать в интернет |
| 22 | SSH |

> **Важно:** сервер отдаёт клиенту `serverPort: 7777`, но на 7777 ничего не слушает. Для VPS задай `MATCH_SERVER_PORT=5050` — клиент проверяет TCP-доступность этого порта перед входом в матч.

---

## 1. Подготовка VPS

```bash
ssh root@ТВОЙ_IP_VPS
```

Обновление и базовые пакеты:

```bash
apt update && apt upgrade -y
apt install -y curl git ufw
```

---

## 2. Node.js 20

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
apt install -y nodejs
node -v   # v20.x
npm -v
```

---

## 3. PostgreSQL (Docker)

```bash
apt install -y docker.io docker-compose-plugin
systemctl enable --now docker
```

Создай пароль (запиши):

```bash
openssl rand -base64 24
# пример: xK9mP2vL8nQ4wR7jT1sA6bC0
```

Запуск Postgres **только на localhost**:

```bash
docker run -d \
  --name shooter-db \
  --restart unless-stopped \
  -e POSTGRES_USER=shooter \
  -e POSTGRES_PASSWORD=ВСТАВЬ_ПАРОЛЬ \
  -e POSTGRES_DB=shooter \
  -p 127.0.0.1:5432:5432 \
  -v shooter_pg_data:/var/lib/postgresql/data \
  postgres:16
```

Проверка:

```bash
docker ps
docker logs shooter-db --tail 20
```

---

## 4. Загрузка сервера на VPS

### Вариант A — git (если репозиторий на GitHub)

```bash
mkdir -p /opt/shooter
cd /opt/shooter
git clone https://github.com/ТВОЙ_ЮЗЕР/ShooterPrototype.git .
cd Backend/QueueService
npm install
```

### Вариант B — копирование с Windows

На своём ПК:

```powershell
scp -r "c:\me\unity\ShooterPrototype\Backend\QueueService" root@ТВОЙ_IP_VPS:/opt/shooter/
```

На VPS:

```bash
cd /opt/shooter/QueueService
npm install
```

---

## 5. Файл окружения `.env`

```bash
nano /opt/shooter/QueueService/.env
```

Содержимое (подставь свои значения):

```env
# PostgreSQL
DATABASE_URL=postgres://shooter:ВСТАВЬ_ПАРОЛЬ@127.0.0.1:5432/shooter

# Публичный IP или домен VPS — то, что видит игрок
MATCH_SERVER_ADDRESS=ТВОЙ_IP_ИЛИ_ДОМЕН
MATCH_SERVER_PORT=5050

# Порты сервиса
PORT=5050
REALTIME_WS_PORT=5051

# BR / очередь (можно оставить так)
MIN_PLAYERS_TO_MATCH=2
TARGET_PLAYERS_PER_MATCH=20
BR_COUNTDOWN_MIN_PLAYERS=2

# Postgres pool
DB_POOL_MAX=20
```

Сохрани: `Ctrl+O`, Enter, `Ctrl+X`.

---

## 6. Первый запуск (проверка)

```bash
cd /opt/shooter/QueueService
export $(grep -v '^#' .env | xargs)
node server.js
```

Ожидаемый вывод:

```text
[db] PostgreSQL profile database ready.
[QueueService] listening on http://127.0.0.1:5050
[QueueService] realtime websocket listening on ws://127.0.0.1:5051
```

В **другом** SSH-окне:

```bash
curl http://127.0.0.1:5050/health
```

Должно быть примерно:

```json
{"ok":true,"database":"postgres", ...}
```

Останови тест: `Ctrl+C`.

---

## 7. Автозапуск (systemd)

```bash
nano /etc/systemd/system/shooter-queue.service
```

```ini
[Unit]
Description=ShooterPrototype QueueService
After=network.target docker.service
Requires=docker.service

[Service]
Type=simple
User=root
WorkingDirectory=/opt/shooter/QueueService
EnvironmentFile=/opt/shooter/QueueService/.env
ExecStart=/usr/bin/node server.js
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Если проект лежит в `/opt/shooter/Backend/QueueService` (вариант git clone), поправь пути:

```ini
WorkingDirectory=/opt/shooter/Backend/QueueService
EnvironmentFile=/opt/shooter/Backend/QueueService/.env
```

```bash
systemctl daemon-reload
systemctl enable shooter-queue
systemctl start shooter-queue
systemctl status shooter-queue
```

Логи:

```bash
journalctl -u shooter-queue -f
```

---

## 8. Firewall

```bash
ufw allow OpenSSH
ufw allow 5050/tcp
ufw allow 5051/tcp
ufw enable
ufw status
```

**Не открывай** 5432 наружу.

---

## 9. Настройка Unity-клиента

В Unity: `Assets/Network/NetworkConfig` (ScriptableObject).

| Поле | Значение |
|------|----------|
| **Queue Api Base Url** | `http://ТВОЙ_IP:5050` |
| **Realtime Ws Url** | `ws://ТВОЙ_IP:5051` |
| **Server Address** | `ТВОЙ_IP` (запасной, если не из NetworkConfig) |
| **Server Port** | `7777` (не критично — матч берёт порт с сервера) |

Пересобери билд игры после изменения.

Проверка с ПК:

```powershell
curl http://ТВОЙ_IP:5050/health
```

---

## 10. Проверка end-to-end

1. Запусти билд игры.
2. Главное меню → «Играть» → очередь.
3. В `journalctl -u shooter-queue -f` должны быть строки `[http][enqueue]`.
4. После матча — вход в игру, WebSocket на 5051.

Создание профиля (опционально):

```powershell
curl -X POST http://ТВОЙ_IP:5050/profile/ensure `
  -H "Content-Type: application/json" `
  -d '{"playerId":"test-player-1"}'
```

Ответ: `"currencyBalance": 100000`. Через `/health` поле `"database"` должно быть `"postgres"`.

---

## 11. Бэкап Postgres (раз в день)

```bash
mkdir -p /opt/shooter/backups
nano /opt/shooter/backup-db.sh
```

```bash
#!/bin/bash
DATE=$(date +%Y%m%d_%H%M)
docker exec shooter-db pg_dump -U shooter shooter | gzip > /opt/shooter/backups/shooter_$DATE.sql.gz
find /opt/shooter/backups -name "*.gz" -mtime +7 -delete
```

```bash
chmod +x /opt/shooter/backup-db.sh
crontab -e
# добавь строку:
0 4 * * * /opt/shooter/backup-db.sh
```

---

## 12. Обновление сервера

```bash
cd /opt/shooter
git pull          # или scp новых файлов
cd Backend/QueueService
npm install
systemctl restart shooter-queue
```

---

## Схема на одном VPS

```
                    Интернет
                       │
              ┌────────▼────────┐
              │   VPS (один)    │
              │                 │
              │  Node :5050 HTTP│◄── Unity (API, профили)
              │  Node :5051 WS  │◄── Unity (игра)
              │                 │
              │  Postgres :5432 │  (только 127.0.0.1)
              │  (Docker)       │
              └─────────────────┘
```

---

## Частые проблемы

| Симптом | Решение |
|---------|---------|
| `database is locked` / SQLite | Нет `DATABASE_URL` в `.env` → проверь `EnvironmentFile` в systemd |
| Не коннектится к серверу | `ufw`, `MATCH_SERVER_ADDRESS` = публичный IP, `MATCH_SERVER_PORT=5050` |
| WS не работает | Открыт 5051, в Unity `ws://IP:5051` |
| `[db] Failed to initialize` | `docker ps`, пароль в `DATABASE_URL` |
| Профили пустые | Вариант A — норма, старый SQLite не переносился |

---

## Чеклист «всё готово»

- [ ] VPS с Ubuntu, Node 20, Docker
- [ ] Postgres в Docker на `127.0.0.1:5432`
- [ ] `.env` с `DATABASE_URL` и `MATCH_SERVER_ADDRESS`
- [ ] `systemctl status shooter-queue` → active
- [ ] `/health` → `"database": "postgres"`
- [ ] Порты 5050, 5051 открыты
- [ ] Unity: `http://IP:5050` и `ws://IP:5051`
- [ ] Игра заходит в очередь и в матч

---

## Связанные файлы

- [README.md](./README.md) — API, переменные окружения, локальный запуск
- `db/schema.postgres.sql` — схема PostgreSQL (создаётся автоматически при старте)
