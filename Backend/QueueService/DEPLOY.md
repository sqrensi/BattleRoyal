# Деплой QueueService: Windows + VPS

Архитектура после рефакторинга:

| Процесс | Порт | Назначение |
|---------|------|------------|
| Master HTTP | **5050** | очередь, профили, `/health` |
| WebSocket | **5051** | realtime: join, pose, shot, snapshot |
| Worker threads | — | 3 воркера × ~50 дуэлей (внутри Node) |
| PostgreSQL | **5432** | только `127.0.0.1` на VPS |

Клиент Unity подключается к **5050** (HTTP) и **5051** (WS).  
В `.env` на VPS обязательно: `MATCH_SERVER_PORT=5050` (не 7777, не 443 без HTTPS).

---

## Часть 1. Локальный запуск на Windows

Для разработки и тестов без VPS.

### 1.1. Node.js

1. Установи [Node.js 20 LTS](https://nodejs.org/) (Windows x64).
2. Проверь в PowerShell:

```powershell
node -v    # v20.x
npm -v
```

> Пакет `better-sqlite3` собирается нативно. Если `npm install` падает с ошибкой компиляции — установи **Visual Studio Build Tools** с workload «Desktop development with C++», затем снова `npm install`.

### 1.2. Установка зависимостей

```powershell
cd "c:\me\unity\ShooterPrototype\Backend\QueueService"
npm install
```

### 1.3. Конфиг (опционально)

```powershell
copy env.windows.example .env
```

По умолчанию без `.env` сервер использует SQLite (`data/shooterprototype.db`) и порты 5050/5051.

### 1.4. Запуск

```powershell
npm start
```

Ожидаемый вывод:

```
[master] http://0.0.0.0:5050 workers=3
[ws] listening ws://0.0.0.0:5051
```

### 1.5. Проверка

```powershell
curl http://127.0.0.1:5050/health
```

Ответ: `"ok": true`, `"database": "sqlite"`, блок `workers`.

Тест очереди:

```powershell
curl -X POST http://127.0.0.1:5050/enqueue -H "Content-Type: application/json" -d "{\"playerId\":\"test-1\"}"
```

### 1.6. Unity Editor → локальный сервер

В `Assets/Network/NetworkConfig`:

| Поле | Значение |
|------|----------|
| Queue Api Base Url | `http://127.0.0.1:5050` |
| Realtime Ws Url | `ws://127.0.0.1:5051` |
| serverAddress | `127.0.0.1` |
| serverPort | `5050` |

Если тестируешь с другого ПК в LAN — подставь IP Windows-машины и открой порты в брандмауэре Windows (5050, 5051 TCP).

### 1.7. Автозапуск на Windows (опционально)

Для постоянного фонового процесса на dev-машине:

```powershell
npm install -g pm2
pm2 start server.js --name shooter-queue --cwd "c:\me\unity\ShooterPrototype\Backend\QueueService"
pm2 save
pm2 startup
```

Логи: `pm2 logs shooter-queue`  
Перезапуск: `pm2 restart shooter-queue`

---

## Часть 2. Деплой на VPS (с Windows)

Продакшен: Ubuntu 22.04+, публичный IP или домен (`api.game35.ru`).

### 2.1. Что копировать с Windows

Из PowerShell на своём ПК:

```powershell
scp -r "c:\me\unity\ShooterPrototype\Backend\QueueService" root@ТВОЙ_IP_VPS:/opt/shooter/
```

Или через Git на VPS (если репозиторий на сервере):

```bash
cd /opt/shooter
git clone <repo-url> .
cd QueueService && npm install
```

**Не копируй** `node_modules` — на VPS выполни `npm install` отдельно.

### 2.2. Пакеты на VPS

```bash
ssh root@ТВОЙ_IP_VPS

apt update
apt install -y curl git ufw docker.io
systemctl enable --now docker
```

### 2.3. Node.js 20 на VPS

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
apt install -y nodejs
node -v
```

### 2.4. PostgreSQL (Docker, только localhost)

```bash
docker run -d \
  --name shooter-db \
  --restart unless-stopped \
  -e POSTGRES_USER=shooter \
  -e POSTGRES_PASSWORD=НАДЁЖНЫЙ_ПАРОЛЬ \
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

### 2.5. Установка QueueService

```bash
mkdir -p /opt/shooter
cd /opt/shooter/QueueService
npm install
```

### 2.6. `.env` на VPS

```bash
nano /opt/shooter/QueueService/.env
```

Минимальный рабочий конфиг:

```env
DATABASE_URL=postgres://shooter:НАДЁЖНЫЙ_ПАРОЛЬ@127.0.0.1:5432/shooter
MATCH_SERVER_ADDRESS=api.game35.ru
MATCH_SERVER_PORT=5050
PORT=5050
REALTIME_WS_PORT=5051
ADDRESSABLES_ROOT=/opt/shooter/QueueService/addressables
WORKER_COUNT=3
MAX_MATCHES_PER_WORKER=50
MAX_CONCURRENT_DUEL_MATCHES=150
SERVER_TICK_RATE=60
SNAPSHOT_RATE_HZ=20
```

| Переменная | Зачем |
|------------|-------|
| `MATCH_SERVER_ADDRESS` | IP или домен, который Unity получит в ticket |
| `MATCH_SERVER_PORT` | **5050** — HTTP API |
| `REALTIME_WS_PORT` | **5051** — WebSocket |
| `WORKER_COUNT` | 3 воркера на 6 CPU (≈50 матчей каждый) |
| `DATABASE_URL` | Postgres; без неё на VPS не запускай прод |

Шаблон: `env.vps.example`.

### 2.7. systemd — автозапуск

```bash
nano /etc/systemd/system/shooter-queue.service
```

```ini
[Unit]
Description=ShooterPrototype QueueService (duel workers)
After=network.target docker.service
Requires=docker.service

[Service]
Type=simple
WorkingDirectory=/opt/shooter/QueueService
EnvironmentFile=/opt/shooter/QueueService/.env
ExecStart=/usr/bin/node server.js
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Путь к node:

```bash
which node   # обычно /usr/bin/node
```

Включение:

```bash
systemctl daemon-reload
systemctl enable shooter-queue
systemctl start shooter-queue
systemctl status shooter-queue
```

Логи:

```bash
journalctl -u shooter-queue -f
journalctl -u shooter-queue -n 50 --no-pager
```

### 2.8. Firewall

```bash
ufw allow OpenSSH
ufw allow 5050/tcp
ufw allow 5051/tcp
ufw enable
ufw status
```

Postgres **не** открывать наружу — только `127.0.0.1:5432`.

### 2.9. Проверка на VPS

```bash
curl http://127.0.0.1:5050/health
```

Ожидается:

```json
{
  "ok": true,
  "database": "postgres",
  "workers": { "count": 3, ... }
}
```

С Windows (внешняя проверка):

```powershell
curl http://api.game35.ru:5050/health
```

---

## Часть 3. Unity (продакшен)

`Assets/Network/NetworkConfig`:

| Поле | Продакшен (HTTP) | Локально |
|------|------------------|----------|
| queueApiBaseUrl | `http://api.game35.ru:5050` | `http://127.0.0.1:5050` |
| realtimeWsUrl | `ws://api.game35.ru:5051` | `ws://127.0.0.1:5051` |
| serverAddress | `api.game35.ru` | `127.0.0.1` |
| serverPort | `5050` | `5050` |

Для **Яндекс Игр** (HTTPS/WSS) — отдельно: [YANDEX-GAMES-HTTPS-CSP.md](./YANDEX-GAMES-HTTPS-CSP.md) (nginx + Let's Encrypt, поля `*Secure`).

Контент в билде — из `Assets/Resources/`, Addressables на VPS не обязательны.

---

## Часть 4. Обновление кода

### С Windows на VPS

```powershell
# вариант 1: scp только изменённых файлов
scp -r "c:\me\unity\ShooterPrototype\Backend\QueueService\server.js" root@ТВОЙ_IP:/opt/shooter/QueueService/
scp -r "c:\me\unity\ShooterPrototype\Backend\QueueService\workers" root@ТВОЙ_IP:/opt/shooter/QueueService/
scp -r "c:\me\unity\ShooterPrototype\Backend\QueueService\network" root@ТВОЙ_IP:/opt/shooter/QueueService/
```

```bash
# на VPS
cd /opt/shooter/QueueService
npm install
systemctl restart shooter-queue
curl http://127.0.0.1:5050/health
```

### Локально на Windows

```powershell
cd "c:\me\unity\ShooterPrototype\Backend\QueueService"
# git pull или копирование файлов
npm install
npm start
# или: pm2 restart shooter-queue
```

---

## Часть 5. Типичные проблемы

| Симптом | Решение |
|---------|---------|
| Unity: `api.game35.ru:443` / connection refused | В `.env`: `MATCH_SERVER_PORT=5050`, не 443. В NetworkConfig — `http://...:5050` |
| `systemctl status` → failed | `journalctl -u shooter-queue -n 50` — часто неверный `DATABASE_URL` или Postgres не запущен |
| Postgres не стартует | `docker ps -a`, `docker start shooter-db` |
| WS не подключается | Открыт ли **5051** в ufw / облачном firewall провайдера |
| `npm install` падает на Windows | Visual Studio Build Tools (C++) |
| Воркеры не поднимаются | Проверь `workers/` на месте; в логах `[worker]` / `WORKER_READY` |
| Высокая нагрузка | Уменьши `WORKER_COUNT` или `MAX_MATCHES_PER_WORKER`; смотри `/health` → metrics |

---

## Быстрая шпаргалка

```text
Windows dev:     npm install → npm start → http://127.0.0.1:5050/health
Windows → VPS:   scp QueueService → npm install → .env → systemctl start
Unity:           HTTP :5050, WS :5051, mode duel only
Прод .env:       DATABASE_URL + MATCH_SERVER_ADDRESS + WORKER_COUNT=3
```

Подробности только по Linux/VPS (краткая версия): [DEPLOY-VPS.md](./DEPLOY-VPS.md).
