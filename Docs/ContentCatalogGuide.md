# Руководство по контенту: скины, кейсы, достижения

Все три системы **data-driven**: правки в JSON + ассеты в `Resources/`. C# и JS менять не нужно, если соблюдены форматы id и пути к файлам.

После изменения JSON на **сервере** перезапустите **QueueService**.

---

## Форматы skin id

| Тип | Папка Resources | Пример id |
|-----|-----------------|-----------|
| Футболка | `Skins/tshirts/004/` | `tshirts_004` |
| Штаны | `Skins/pants/004/` | `pants_004` |
| Обувь | `Skins/shoes/004/` | `shoes_004` |
| Перчатки | `Skins/gloves/004/` | `gloves_004` |
| Лицо | `Attachments/face/004/` | `attachment_face_004` |
| Волосы | `Attachments/hair/004/` | `attachment_hair_004` |
| AK | `WeaponSkins/Ak-47/002/` | `weapon_ak47_002` |
| Сniper | `WeaponSkins/sniper/002/` | `weapon_sniper_002` |
| Pistol | `WeaponSkins/pistol/002/` | `weapon_pistol_002` |
| MP7 | `WeaponSkins/mp7/002/` | `weapon_mp7_002` |

Минимум для иконки: **`picture.png`**. Для одежды — ещё `prefab` и `material`. Для оружия (не `000`) — **`material.mat`**.

---

## Скин только в магазине

1. Добавьте ассеты в `Assets/Resources/...`
2. В **оба** файла:
   - `Assets/Resources/Shop/skin-catalog.json`
   - `Backend/QueueService/data/skin-catalog.json`

```json
"shopItems": [
  { "skinId": "weapon_sniper_002", "price": 4500 }
]
```

3. Перезапуск QueueService.

Бесплатный стартовый скин — добавьте id в `defaultOwnedSkinIds`.

---

## Скин только в кейсе (лут)

1. Ассеты в Resources
2. В **оба** `case-catalog.json` — в `lootPool` нужного кейса:

```json
"lootPool": [
  "weapon_sniper_002"
]
```

3. Перезапуск QueueService.

В `shopItems` добавлять **не** нужно.

---

## Скин и в магазине, и в кейсе

Оба JSON: запись в `shopItems` + id в `lootPool`.

---

## Кейс в магазине

1. Картинка: `Assets/Resources/Cases/002/picture.png`
2. В **оба** `case-catalog.json`:

```json
"shopCases": [
  {
    "caseId": "case_002",
    "displayName": "Кейс одежды",
    "pictureFolder": "002",
    "price": 3000,
    "lootPool": [
      "tshirts_002",
      "pants_002"
    ]
  }
]
```

| Поле | Описание |
|------|----------|
| `caseId` | Уникальный id (`case_001`, `case_002`, …) |
| `displayName` | Название в UI |
| `pictureFolder` | Папка в `Resources/Cases/` |
| `price` | Цена покупки в магазине |
| `lootPool` | Список `skinId`; шанс у всех равный |

**Покупка:** магазин → кейс попадает в **инвентарь (вкладка «Кейсы»)**.  
**Открытие:** инвентарь → кнопка **«Открыть»** → анимация → скин в инвентарь.

---

## Достижения

Файлы (синхронизировать оба):

- `Assets/Resources/Shop/achievement-catalog.json`
- `Backend/QueueService/data/achievement-catalog.json`

### Пример

```json
{
  "achievements": [
    {
      "achievementId": "ach_kill_player",
      "code": "kill_player",
      "title": "Первый фраг",
      "description": "Убейте одного игрока",
      "target": 1,
      "sortOrder": 1,
      "eventType": "kill_player",
      "reward": {
        "type": "currency",
        "amount": 5000
      }
    },
    {
      "achievementId": "ach_plane_landed",
      "code": "plane_landed",
      "title": "Высадка",
      "description": "Высадитесь из самолёта",
      "target": 1,
      "sortOrder": 2,
      "eventType": "plane_landed",
      "reward": {
        "type": "case",
        "caseId": "case_001",
        "amount": 1
      }
    }
  ]
}
```

### Поля

| Поле | Описание |
|------|----------|
| `achievementId` | Уникальный id |
| `code` | Внутренний код (для отладки) |
| `title` | Заголовок в UI |
| `description` | Описание |
| `target` | Сколько раз нужно выполнить событие |
| `sortOrder` | Порядок в списке |
| `eventType` | Событие прогресса (см. ниже) |
| `reward.type` | `currency` или `case` |
| `reward.amount` | Монеты или количество кейсов |
| `reward.caseId` | Для `case` — id из `case-catalog.json` |

### Встроенные eventType

| eventType | Когда засчитывается |
|-----------|---------------------|
| `kill_player` | Локальный игрок получил убийство в матче |
| `plane_landed` | Локальный игрок высадился из самолёта (BR) |

Чтобы добавить **новый тип события**, нужно один раз в коде вызвать  
`MatchAchievementReporter.ReportEvent("ваш_event_type")` в нужном месте игры.  
После этого достижения с этим `eventType` в JSON работают без других правок.

### Награды

- **`currency`** — монеты на баланс после «Забрать награду»
- **`case`** — кейс в инвентарь (открывается отдельно)

### UI

- Вкладка **«Достижения»** в главном меню
- Выполненное — подсветка + кнопка **«Забрать награду»**
- После получения награды достижение **исчезает** из списка
- В матче при выполнении — **оповещение слева выше середины** (~5 сек)

---

## Чеклист

| Действие | skin-catalog | case-catalog | achievement-catalog | Resources | Restart Queue |
|----------|:------------:|:------------:|:-------------------:|:---------:|:-------------:|
| Скин в магазин | ✅ | — | — | ✅ | ✅ |
| Скин в кейс | — | ✅ lootPool | — | ✅ | ✅ |
| Новый кейс в магазин | — | ✅ shopCases | — | ✅ картинка | ✅ |
| Новое достижение | — | — | ✅ | — | ✅ |
