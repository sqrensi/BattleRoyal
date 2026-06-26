# Деплой на VPS (кратко)

Полная инструкция (Windows dev + деплой с Windows на VPS + Unity): **[DEPLOY.md](./DEPLOY.md)**

---

## Минимум для продакшена

```bash
# VPS
apt install -y docker.io curl
curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
apt install -y nodejs

docker run -d --name shooter-db --restart unless-stopped \
  -e POSTGRES_USER=shooter -e POSTGRES_PASSWORD=ПАРОЛЬ -e POSTGRES_DB=shooter \
  -p 127.0.0.1:5432:5432 -v shooter_pg_data:/var/lib/postgresql/data postgres:16

mkdir -p /opt/shooter
# scp QueueService с Windows → /opt/shooter/
cd /opt/shooter/QueueService && npm install
```

`.env` — см. `env.vps.example`.

```bash
systemctl enable --now shooter-queue   # unit из DEPLOY.md §2.7
ufw allow 5050/tcp && ufw allow 5051/tcp && ufw enable
curl http://127.0.0.1:5050/health
```

## С Windows

```powershell
scp -r "c:\me\unity\ShooterPrototype\Backend\QueueService" root@IP:/opt/shooter/
```

## Порты

- **5050** — HTTP (enqueue, profiles, health)
- **5051** — WebSocket (дуэль realtime)
- **5432** — Postgres только localhost

## Обновление

```bash
cd /opt/shooter/QueueService && npm install && systemctl restart shooter-queue
```
