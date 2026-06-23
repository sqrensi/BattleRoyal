# Яндекс Игры: HTTPS, домен и CSP

Подробная инструкция, как подключить **игровой сервер** (профиль, матчмейкинг, WebSocket) к WebGL-билду на Яндекс Играх.

> **Контент (скины, звуки, персонажи)** сейчас лежит в локальном билде (`Assets/Resources/`).  
> На VPS нужна только **API-логика** QueueService — Addressables на сервер заливать не нужно.

---

## Зачем это нужно

Игра на Яндекс Играх открывается по **HTTPS**. Браузер и политика **CSP** (Content Security Policy) платформы:

- **блокируют** запросы на `http://83.220.165.44:5050` (mixed content + не HTTPS);
- **не разрешают** произвольные IP-адреса в `connect-src`;
- **не разрешают** порты вроде `:5050` в URL клиента — только домен на стандартном HTTPS (443).

Типичные ошибки в F12:

```text
violates Content Security Policy directive: connect-src ...
Connecting to 'http://83.220.165.44:5050/...' ...
```

Пока CSP не одобрен и нет HTTPS-домена — в меню будет **«Сервер недоступен»**, матчмейкинг не заработает.

---

## Общая схема

```text
Браузер (yandex.ru/games, HTTPS)
        │
        │  https://api.mygame.ru/profile/ensure
        │  wss://api.mygame.ru/ws
        ▼
   Nginx :443 (Let's Encrypt)
        │
        ├── /      → 127.0.0.1:5050  (QueueService HTTP API)
        └── /ws    → 127.0.0.1:5051  (QueueService WebSocket)
```

Снаружи клиент видит **только домен**. Порты 5050/5051 остаются внутри VPS.

---

## Шаг 1. Домен

### 1.1. Купить или использовать существующий домен

Пример: `mygame.ru`, поддомен для API: **`api.mygame.ru`**.

Яндекс в CSP принимает **имя хоста**, не IP. Вариант `https://83.220.165.44` **не подойдёт**.

### 1.2. DNS-запись

В панели регистратора домена:

| Тип | Имя | Значение        | TTL  |
|-----|-----|-----------------|------|
| A   | api | 83.220.165.44   | 300  |

(Подставь свой IP VPS.)

### 1.3. Проверка

С Windows:

```powershell
nslookup api.mygame.ru
```

Должен вернуться IP VPS. Подожди 5–30 минут после смены DNS.

---

## Шаг 2. Nginx + Let's Encrypt на VPS

SSH на сервер:

```bash
ssh root@83.220.165.44
```

### 2.1. Установка nginx и certbot

```bash
apt update
apt install -y nginx certbot python3-certbot-nginx
```

### 2.2. Firewall

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 5050/tcp   # опционально, для прямого HTTP-теста
ufw allow 5051/tcp   # опционально
ufw enable
```

Порты 5050/5051 наружу можно **закрыть**, если весь трафик идёт через nginx на 443.

### 2.3. Убедиться, что QueueService запущен

```bash
systemctl status shooter-queue
curl http://127.0.0.1:5050/health
```

Ответ: `"ok": true`.

### 2.4. Конфиг nginx

Создай файл `/etc/nginx/sites-available/shooter-api`:

```nginx
server {
    listen 80;
    server_name api.game35.ru;

    location / {
        return 301 https://$host$request_uri;
    }
}

server {
    listen 443 ssl http2;
    server_name api.game35.ru;

    # Certbot подставит пути после шага 2.5
    ssl_certificate     /etc/letsencrypt/live/api.game35.ru/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.game35.ru/privkey.pem;

    # API (профиль, очередь, health)
    location / {
        proxy_pass http://127.0.0.1:5050;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # WebSocket (realtime в матче)
    location /ws {
        proxy_pass http://127.0.0.1:5051;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 86400;
    }
}
```

Замени `api.mygame.ru` на свой домен.

Включи сайт:

```bash
ln -sf /etc/nginx/sites-available/shooter-api /etc/nginx/sites-enabled/
nginx -t
systemctl reload nginx
```

### 2.5. SSL-сертификат

```bash
certbot --nginx -d api.mygame.ru
```

Следуй подсказкам (email, согласие). Certbot сам обновит конфиг nginx.

Автопродление:

```bash
certbot renew --dry-run
```

### 2.6. Проверка HTTPS

На VPS:

```bash
curl https://api.mygame.ru/health
```

На Windows:

```powershell
Invoke-WebRequest -Uri "https://api.mygame.ru/health" -UseBasicParsing
```

Должен быть JSON с `"ok": true`.

Проверка WebSocket (опционально, нужен `wscat`):

```bash
npm install -g wscat
wscat -c wss://api.mygame.ru/ws
```

---

## Шаг 3. Консоль разработчика Яндекс Игр — «Правила для CSP»

Без одобрения домена Яндекс **не добавит** его в `connect-src`, и браузер заблокирует запросы.

### 3.1. Где настраивать

1. [Консоль разработчика Яндекс Игр](https://games.yandex.ru/console)
2. Выбери игру
3. Вкладка **«Настройки»** / **«Правила для CSP»** (название может немного отличаться)

Документация: [Upload a game — allowed hosts](https://yandex.com/dev/games/doc/en/console/add-new-game)

### 3.2. Что указать в заявке

| Поле | Пример |
|------|--------|
| Хост | `api.mygame.ru` |
| Обоснование | Игровой сервер: синхронизация профиля игрока (скины, валюта, никнейм), матчмейкинг, WebSocket-синхронизация в матче |

**Не указывай:**

- протокол (`https://`) — подразумевается автоматически;
- порт (`:5050`);
- путь (`/profile/...`);
- IP-адрес.

### 3.3. Ограничения модерации

- На внешнем сервере должна быть **игровая логика**, а не «почти весь билд».
- Сейчас контент локальный — это **плюс** для модерации.
- Сервер должен стабильно отвечать из регионов, где игра опубликована.

Дождись **одобрения** заявки перед финальным тестом на проде.

---

## Шаг 4. NetworkConfig в Unity

Файл: `Assets/Network/NetworkConfig.asset`  
(Inspector: **Shooter Prototype → Network Config** или объект с `NetworkConfig` в сцене.)

### 4.1. Поля для локальной разработки (HTTP)

| Поле | Значение |
|------|----------|
| Queue Api Base Url | `http://83.220.165.44:5050` |
| Realtime Ws Url | `ws://83.220.165.44:5051` |

Используются при тесте через `http://localhost:8080` (без CSP).

### 4.2. Поля для Яндекс Игр (HTTPS) — **обязательно**

| Поле | Значение |
|------|----------|
| Queue Api Base Url Secure | `https://api.mygame.ru` |
| Realtime Ws Url Secure | `wss://api.mygame.ru/ws` |

**Без `:5050` и без слэша в конце.**

В WebGL-билде на HTTPS-странице код автоматически выбирает Secure-URL (`NetworkConfig.ResolveQueueApiBaseUrl()` / `ResolveRealtimeWsUrl()`).

### 4.3. Пример YAML (для справки)

```yaml
queueApiBaseUrl: http://83.220.165.44:5050
queueApiBaseUrlSecure: https://api.mygame.ru
realtimeWsUrl: ws://83.220.165.44:5051
realtimeWsUrlSecure: wss://api.mygame.ru/ws
```

---

## Шаг 5. Сборка WebGL в Unity

1. **File → Build Settings → WebGL**
2. Убедись, что в билде сцены: MainMenu, Game, 1x1
3. **Player Settings → WebGL:**
   - **Development Build** — выключен (для продакшена)
   - **Compression Format** — Brotli или Disabled (см. DEPLOY-VPS / локальный тест)
4. **Build** → папка билда (например `Build/WebGL`)

Контент из `Resources` попадёт в билд автоматически. Отдельно Addressables собирать **не нужно**.

---

## Шаг 6. Загрузка на Яндекс Игры

1. Упакуй билд в `.zip` (как требует консоль).
2. Загрузи в **Консоль разработчика → Черновик**.
3. Открой черновик игры на `yandex.ru/games` и проверь:
   - меню загружается;
   - профиль синхронизируется (нет «Сервер недоступен»);
   - матчмейкинг находит матч.

### Проверка в F12

**Network:**

- `POST https://api.mygame.ru/profile/ensure` → **200**
- `GET https://api.mygame.ru/health` → **200**

**Console:**

- нет ошибок `Content Security Policy` / `connect-src` для твоего домена.

---

## Локальное тестирование

### Вариант A — без CSP (быстро)

```powershell
cd c:\me\unity\ShooterPrototype\Build\WebGL
python -m http.server 8080
```

Открой `http://localhost:8080`.  
Используются HTTP-URL из NetworkConfig (`queueApiBaseUrl`, не Secure).

### Вариант B — с CSP как на Яндексе

Пакет [@yandex-games/sdk-dev-proxy](https://yandex.com/dev/games/doc/en/concepts/local-launch):

```bash
npx @yandex-games/sdk-dev-proxy --app-id=ТВОЙ_APP_ID --csp
```

Так можно поймать CSP-ошибки **до** публикации.

---

## Чеклист перед публикацией

- [ ] DNS: `api.mygame.ru` → IP VPS
- [ ] `curl https://api.mygame.ru/health` → ok
- [ ] QueueService: `systemctl status shooter-queue` → active
- [ ] NetworkConfig: заполнены **Secure**-поля с доменом
- [ ] WebGL билд пересобран после смены NetworkConfig
- [ ] В консоли Яндекса одобрен хост в **«Правила для CSP»**
- [ ] В F12 на черновике нет CSP-ошибок на `api.mygame.ru`
- [ ] Профиль и матчмейкинг работают

---

## Частые проблемы

### «Сервер недоступен» в меню

| Причина | Решение |
|---------|---------|
| Secure-URL пустой в NetworkConfig | Заполни `queueApiBaseUrlSecure` |
| CSP не одобрен | Дождись заявки в консоли Яндекса |
| nginx не проксирует | `curl https://api.mygame.ru/health` |
| Старый билд | Пересобери WebGL после смены config |

### CSP в консоли на `http://83.220.165.44:5050`

Клиент всё ещё использует IP/HTTP. Проверь Secure-поля и пересобери билд.

### `profile/ensure` → 500

Смотри логи на VPS:

```bash
journalctl -u shooter-queue -n 100 --no-pager
```

### WebSocket не подключается в матче

- Проверь `wss://api.mygame.ru/ws` через wscat
- Убедись, что `realtimeWsUrlSecure` совпадает с nginx `location /ws`
- Домен должен быть в CSP (тот же хост, что и API)

### Brotli / `.br` не грузится локально

Для локального `python -m http.server` отключи Brotli в Player Settings или используй сервер с заголовком `Content-Encoding: br`. Подробнее — в `DEPLOY-VPS.md`.

---

## Связанные файлы проекта

| Файл | Назначение |
|------|------------|
| `Assets/Network/NetworkConfig.asset` | URL API и WebSocket |
| `Assets/Scripts/Network/NetworkConfig.cs` | Логика выбора HTTP/HTTPS |
| `Backend/QueueService/server.js` | HTTP :5050, WS :5051 |
| `Backend/QueueService/DEPLOY-VPS.md` | Деплой VPS, Postgres, systemd |
| `tools/test-vps.ps1` | Быстрая проверка `/health` и API |

---

## Краткая шпаргалка (6 шагов)

1. **Домен** `api.mygame.ru` → A-запись на VPS  
2. **Nginx + Let's Encrypt** → HTTPS на 443, прокси на 5050/5051  
3. **Яндекс: «Правила для CSP»** → добавить `api.mygame.ru`  
4. **NetworkConfig** → `queueApiBaseUrlSecure`, `realtimeWsUrlSecure`  
5. **Unity** → WebGL Build  
6. **Загрузить** zip на Яндекс Игры и проверить черновик  
