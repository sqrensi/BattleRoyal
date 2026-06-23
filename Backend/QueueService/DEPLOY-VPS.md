# Деплой на VPS (PostgreSQL + QueueService)

Ubuntu 22.04, один публичный IP. Порты наружу: **5050** (HTTP), **5051** (WebSocket). Postgres только на `127.0.0.1`.

> В `.env` обязательно: `MATCH_SERVER_PORT=5050` (не 7777).

---

## 1. Пакеты

```bash
ssh root@ТВОЙ_IP_VPS

apt update
apt install -y curl git ufw docker.io
systemctl enable --now docker
```

---

## 2. Node.js 20

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
apt install -y nodejs
```

---

## 3. PostgreSQL

```bash
docker run -d \
  --name shooter-db \
  --restart unless-stopped \
  -e POSTGRES_USER=shooter \
  -e POSTGRES_PASSWORD=ПАРОЛЬ \
  -e POSTGRES_DB=shooter \
  -p 127.0.0.1:5432:5432 \
  -v shooter_pg_data:/var/lib/postgresql/data \
  postgres:16
```

---

## 4. QueueService на сервер

На VPS (SSH) — создай папку:

```bash
mkdir -p /opt/shooter
```

С Windows:

```powershell
scp -r "c:\me\unity\ShooterPrototype\Backend\QueueService" root@ТВОЙ_IP_VPS:/opt/shooter/
```

На VPS:

```bash
cd /opt/shooter/QueueService
npm install
```

---

## 5. `.env`

```bash
nano /opt/shooter/QueueService/.env
```

```env
DATABASE_URL=postgres://shooter:ПАРОЛЬ@127.0.0.1:5432/shooter
MATCH_SERVER_ADDRESS=83.220.165.44
MATCH_SERVER_PORT=5050
PORT=5050
ADDRESSABLES_ROOT=/opt/shooter/QueueService/addressables
```

---

## 6. systemd (автозапуск сервера)

**Зачем:** чтобы QueueService работал **в фоне** и **перезапускался** после ребута VPS (без ручного `node server.js`).

### 6.1. Создай файл службы

На VPS:

```bash
nano /etc/systemd/system/shooter-queue.service
```

Файл **новый**, в nano будет пусто — вставь **целиком**:

```ini
[Unit]
Description=ShooterPrototype QueueService
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

Сохрани: `Ctrl+O` → Enter → `Ctrl+X`.

| Строка | Смысл |
|--------|--------|
| `After=... docker.service` | Сначала Docker (Postgres), потом Node |
| `WorkingDirectory=...` | Папка с `server.js` (как в п. 4) |
| `EnvironmentFile=...` | Подхватить `.env` из п. 5 |
| `Restart=always` | Упал — systemd поднимет снова через 5 сек |

Проверь путь к node (если позже не стартует):

```bash
which node
```

Должно быть `/usr/bin/node`. Если другой путь — пропиши его в `ExecStart=`.

### 6.2. Включи службу

```bash
systemctl daemon-reload
systemctl enable shooter-queue
systemctl start shooter-queue
```

| Команда | Что делает |
|---------|------------|
| `daemon-reload` | systemd перечитал новый файл |
| `enable` | Запуск при загрузке VPS |
| `start` | Запустить сейчас |

### 6.3. Проверь статус

```bash
systemctl status shooter-queue
```

Нормально: **`Active: active (running)`** и в логах строки про `listening on ...5050` и `5051`.

Выход из просмотра: `q`.

Если **`failed`**:

```bash
journalctl -u shooter-queue -n 50 --no-pager
```

Частые причины: нет `.env`, неверный пароль в `DATABASE_URL`, Postgres не запущен (`docker ps`).

### 6.4. Полезные команды потом

```bash
systemctl restart shooter-queue   # после обновления кода
systemctl stop shooter-queue      # остановить
journalctl -u shooter-queue -f    # логи в реальном времени
```

---

## 7. Firewall

```bash
ufw allow OpenSSH
ufw allow 5050/tcp
ufw allow 5051/tcp
ufw enable
```

---

## 8. Unity

`Assets/Network/NetworkConfig`:

- **Queue Api Base Url** → `http://ТВОЙ_IP:5050` (локальный тест через `http://localhost:8080`)
- **Queue Api Base Url Secure** → `https://api.ТВОЙ_ДОМЕН` (для Яндекс Игр, без `:5050`)
- **Realtime Ws Url** → `ws://ТВОЙ_IP:5051`
- **Realtime Ws Url Secure** → `wss://api.ТВОЙ_ДОМЕН/ws`
### Яндекс Игры (HTTPS + CSP)

Подробная пошаговая инструкция: **[YANDEX-GAMES-HTTPS-CSP.md](./YANDEX-GAMES-HTTPS-CSP.md)**

Кратко: домен → nginx + Let's Encrypt → заявка в «Правила для CSP» → Secure-поля в NetworkConfig → WebGL build.

### Контент (локальный билд)

Скины, звуки, персонажи, кейсы и т.д. лежат в **`Assets/Resources/`** и попадают в WebGL-билд через `Resources.Load`.  
Remote Addressables **отключены** — заливать `ServerData/WebGL` на VPS не нужно.

Опционально для уменьшения билда: **Shooter Prototype → Build Optimization → Rebuild Controllers Blink-Only And Remove Opsive**.

Пересобери WebGL-билд после изменений в Resources.

---

## Проверка

```bash
curl http://127.0.0.1:5050/health
```

`"database": "postgres"`, `"ok": true`.

Логи: `journalctl -u shooter-queue -f`

---

## Обновление кода

```bash
cd /opt/shooter/QueueService
# scp новых файлов или git pull
npm install
systemctl restart shooter-queue
```
