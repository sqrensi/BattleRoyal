# Руководство по модульной одежде в Blender

Этот документ описывает, как подготовить одежду для текущего remote-персонажа в проекте.

Главное правило: одежда должна быть привязана к тому же скелету, что и текущий персонаж. Недостаточно просто положить mesh поверх тела. Тест с Genies выглядел плохо именно потому, что одежда сделана под другой rig, другие пропорции и другой bind pose. Unity не может нормально “натянуть” такую одежду на текущий remote-аватар без правильного рескиннинга.

## Текущая схема персонажа в проекте

Remote игроки собираются вокруг такой иерархии:

```text
RemotePlayerRoot
  ThirdPersonBody
    SyntyVisual
      Character mesh
      Armature / bones
```

Активная модель персонажа сейчас берётся отсюда:

```text
Assets/Resources/Characters/Ch18_nonPBR.fbx
```

Remote presentation, weapon IK, hitbox, look pitch posture и анимации ожидают, что визуал персонажа находится здесь:

```text
ThirdPersonBody/SyntyVisual
```

В коде skinned mesh перепривязывается к костям по именам:

```text
CharacterModelApplier.RebindSkinnedMeshToInstance(...)
```

Это значит, что mesh одежды должен использовать те же имена костей, что и `Ch18_nonPBR.fbx`.

## Рекомендуемый подход

Есть два нормальных варианта.

### Вариант A: цельный outfit

Это самый безопасный вариант для remote игроков.

Вместо отдельных hoodie, pants и shoes создаётся один цельный full-body outfit:

```text
Outfit_Combat_01
  Body/clothing combined mesh
  Same skeleton as Ch18_nonPBR
```

Плюсы:

- меньше clipping
- проще добиться нормального вида remote игроков
- легко скрыть оригинальное тело
- хорошо читается в мультиплеере

Минусы:

- меньше свободы кастомизации
- нельзя свободно комбинировать верх, низ и обувь

Если нужна быстрая и чистая реализация, начинай именно с этого варианта.

### Вариант B: модульные слоты одежды

Создаются отдельные skinned meshes:

```text
Torso_Hoodie_01
Legs_Pants_01
Feet_Boots_01
Hands_Gloves_01
Head_Helmet_01
```

Плюсы:

- можно делать кастомизацию
- можно смешивать hoodie, pants, boots и другие части

Минусы:

- больше проблем с clipping
- нужно скрывать части тела под одеждой
- нужны строгие правила для контента

Для текущего проекта лучше сначала сделать один цельный outfit, а потом расширять систему до модульных слотов.

## Blender workflow

### 1. Импортируй базового персонажа

В Blender:

1. `File -> Import -> FBX`
2. Выбери файл:

```text
Assets/Resources/Characters/Ch18_nonPBR.fbx
```

Правила импорта:

- не переименовывай armature
- не переименовывай bones
- не применяй scale к armature после импорта
- не вращай skeleton
- не удаляй deform bones
- сохрани исходную rest pose

Одежду нужно создавать вокруг этого импортированного тела.

## 2. Подготовь сцену

После импорта у тебя должны быть:

```text
Armature
Character mesh
```

Перед созданием одежды:

1. Убедись, что персонаж в rest pose.
2. Проверь transforms:
   - scale body mesh обычно должен быть `1, 1, 1`
   - scale armature обычно должен быть `1, 1, 1`
3. Сохрани рабочий `.blend` файл:

```text
ShooterPrototype_Clothing_Workfile.blend
```

Этот файл будет исходником для будущей одежды.

## 3. Создай mesh одежды

В Blender одежду можно делать двумя способами:

```text
Способ A: сделать одежду из копии тела
Способ B: смоделировать одежду отдельным mesh поверх тела
```

Если ты пока не силён в Blender, начинай со **способа A**. Он проще, потому что одежда сразу повторяет форму персонажа.

### 3.1. Подготовь body mesh как основу

После импорта `Ch18_nonPBR.fbx` найди объект с телом персонажа.

Обычно в сцене будут:

```text
Armature
Character mesh
```

Тебе нужен именно `Character mesh`, то есть объект, на котором есть геометрия тела.

Как проверить:

1. Кликни по объекту тела.
2. Перейди в `Edit Mode` клавишей `Tab`.
3. Если видишь вершины/полигоны тела, это нужный объект.
4. Вернись в `Object Mode` клавишей `Tab`.

Перед началом удобно включить отображение wireframe:

```text
Viewport Overlays -> Wireframe
```

Так будет легче видеть, где одежда пересекается с телом.

### 3.2. Способ A: сделать одежду из копии тела

Этот способ лучше всего подходит для первой попытки.

Идея: мы копируем часть тела, например torso и arms, отделяем её в новый объект и превращаем в hoodie.

#### Шаг 1: продублируй body mesh

1. Выбери body mesh.
2. Нажми `Shift + D`.
3. Не двигай мышкой.
4. Нажми `Enter`.

Теперь у тебя две одинаковые копии тела.

Переименуй новую копию:

```text
CL_Torso_Hoodie_01
```

Как переименовать:

1. Выбери объект.
2. В правой панели открой `Object Properties`.
3. В поле имени введи `CL_Torso_Hoodie_01`.

#### Шаг 2: удали лишние части

Теперь из копии тела нужно оставить только область будущей одежды.

Для hoodie оставь:

```text
torso
upper arms
forearms, если hoodie с рукавами
neck opening area
```

Удали:

```text
head
hands, если не нужны
legs
feet
лишние внутренние части
```

Как удалять:

1. Выбери `CL_Torso_Hoodie_01`.
2. Нажми `Tab`, чтобы перейти в `Edit Mode`.
3. Включи выбор faces:

```text
клавиша 3
```

4. Выделяй лишние полигоны.
5. Нажми `X`.
6. Выбери `Faces`.

Если сложно выделять:

- включи `X-Ray` кнопкой в правом верхнем углу viewport
- используй `Box Select` клавишей `B`
- используй `Circle Select` клавишей `C`

#### Шаг 3: сделай одежду чуть больше тела

Одежда не должна лежать точно в тех же координатах, что тело, иначе будет мерцание и clipping.

Простой способ:

1. В `Edit Mode` выдели все вершины одежды:

```text
A
```

2. Нажми:

```text
Alt + S
```

3. Медленно двигай мышкой наружу.
4. Сделай небольшой offset.

Обычно достаточно:

```text
0.01 - 0.04 метра
```

Если одежда стала слишком “надутой”, откати `Ctrl + Z` и повтори с меньшим значением.

#### Шаг 4: оформи края hoodie

После удаления лишних faces у hoodie будут открытые края:

```text
neck opening
waist opening
sleeve ends
```

Чтобы края выглядели аккуратно:

1. В `Edit Mode` выбери edge select:

```text
клавиша 2
```

2. Alt-click по краю, чтобы выделить edge loop.
3. Сделай небольшой extrude внутрь:

```text
E
```

4. Потом scale:

```text
S
```

Так можно сделать манжеты, нижний край hoodie и ворот.

Для новичка можно сначала не делать идеальные края. Главное, чтобы mesh не рвался и нормально двигался.

#### Шаг 5: добавь толщину через Solidify

Одежда из копии тела будет тонкой, как бумага. Для толщины:

1. Выбери hoodie object.
2. Перейди в `Modifiers`.
3. Add Modifier -> `Solidify`.
4. Поставь:

```text
Thickness: 0.01 - 0.03
Offset: 1
```

Если одежда начинает сильно залезать в тело, попробуй:

```text
Offset: 0
```

или уменьши `Thickness`.

Для game-ready одежды часто можно оставить очень маленькую толщину или вообще не использовать `Solidify`, если камера далеко.

### 3.3. Способ B: смоделировать одежду поверх тела

Этот способ чище, но сложнее.

Подходит, если ты хочешь нормальную форму hoodie, куртки или бронежилета.

#### Шаг 1: создай простой mesh

Например для hoodie:

1. Add -> Mesh -> Cube.
2. Переименуй:

```text
CL_Torso_Hoodie_01
```

3. Перейди в `Edit Mode`.
4. Начни вытягивать форму вокруг torso.

Но новичку обычно проще не начинать с cube, а использовать способ A.

#### Шаг 2: используй Shrinkwrap

Если mesh одежды примерно повторяет форму тела:

1. Выбери одежду.
2. Add Modifier -> `Shrinkwrap`.
3. В поле `Target` выбери body mesh.
4. Поставь:

```text
Mode: Nearest Surface Point
Offset: 0.015 - 0.04
```

`Offset` нужен, чтобы одежда была поверх тела, а не внутри него.

После настройки можно применить modifier:

```text
Apply
```

Но лучше применяй только когда результат тебя устраивает.

### 3.4. Как сделать pants

Для pants лучше тоже начать с копии тела.

#### Шаг 1: продублируй тело

1. Выбери body mesh.
2. `Shift + D`.
3. `Enter`.
4. Переименуй копию:

```text
CL_Legs_Pants_01
```

#### Шаг 2: оставь только ноги

В `Edit Mode` оставь:

```text
hips
thighs
calves
ankle area
```

Удали:

```text
torso
arms
head
feet, если shoes будут отдельным слотом
```

Если pants должны закрывать обувь, можно оставить нижнюю часть до стоп.

#### Шаг 3: расширь pants наружу

Выдели все vertices pants:

```text
A
```

Потом:

```text
Alt + S
```

Расширь чуть наружу.

Для pants обычно нужно больше пространства вокруг:

```text
knees
hips
crotch
```

Особенно важно оставить место в районе коленей, иначе при беге mesh будет ломаться.

### 3.5. Как сделать boots

Для boots можно начинать с feet области.

Самый простой вариант:

1. Дублируй body mesh.
2. Оставь только feet/ankle area.
3. Расширь через `Alt + S`.
4. Сделай форму ботинка через move/scale vertices.

Лучше, если boots закрывают ступню полностью. Тогда в Unity можно скрыть base feet.

### 3.6. Что такое хорошая topology для одежды

Topology — это то, как идут полигоны.

Для одежды на персонаже важно, чтобы loops шли вокруг суставов:

```text
shoulder loops
elbow loops
waist loops
knee loops
ankle loops
```

Плохой вариант:

```text
длинные растянутые треугольники на локте/колене
очень мало полигонов в месте сгиба
хаотичная сетка вокруг плеча
```

Хороший вариант:

```text
несколько edge loops вокруг локтя
несколько edge loops вокруг колена
чистые кольца вокруг рукавов
чистый край у воротника
```

Для первого прототипа не нужно идеально. Главное:

- одежда не рвётся
- сильно не проваливается в тело
- нормально двигается в idle/run/crouch

### 3.7. Как быстро проверить clipping

В Blender:

1. Оставь body visible.
2. Оставь clothing visible.
3. Включи material preview.
4. Вращай камеру вокруг персонажа.

Проверь:

```text
грудь
спина
плечи
локти
таз
колени
лодыжки
```

Если тело пробивает одежду:

- выдели проблемные vertices одежды
- нажми `Alt + S`
- немного выдвинь наружу

Если одежда слишком широкая:

- выдели vertices
- нажми `S`
- немного уменьши

### 3.8. Нужно ли удалять тело под одеждой в Blender

Для full outfit — да, лучше делать так, чтобы outfit сам заменял тело.

Для modular clothing есть два варианта:

```text
Вариант 1: оставить тело, а в Unity скрывать body parts
Вариант 2: удалить закрытые части тела прямо в full outfit mesh
```

Если ты делаешь первый рабочий prototype, делай full outfit:

```text
один mesh, который закрывает всё тело
оригинальное тело в Unity скрывается
```

Так будет меньше проблем.

### 3.9. Практический рецепт для первого hoodie

Вот самый простой порядок:

1. Импортируй `Ch18_nonPBR.fbx`.
2. Выбери body mesh.
3. `Shift + D`, потом `Enter`.
4. Переименуй копию в `CL_Torso_Hoodie_01`.
5. Перейди в `Edit Mode`.
6. Удали head, legs, feet.
7. Оставь torso и arms.
8. Нажми `A`, чтобы выделить всё.
9. Нажми `Alt + S`.
10. Чуть расширь одежду наружу.
11. Добавь `Solidify` с `Thickness 0.01 - 0.02`.
12. Проверь shoulders/elbows.
13. Если пробивает тело, вручную выдвинь vertices наружу.
14. Сохрани `.blend`.
15. Переходи к разделу 4: переносу weights.

### 3.10. Практический рецепт для первых pants

1. Выбери body mesh.
2. `Shift + D`, потом `Enter`.
3. Переименуй копию в `CL_Legs_Pants_01`.
4. Перейди в `Edit Mode`.
5. Удали torso, arms, head.
6. Оставь hips и legs.
7. Нажми `A`.
8. Нажми `Alt + S`.
9. Расширь pants наружу.
10. Особенно проверь knees и crotch.
11. Добавь `Solidify`, если нужна толщина.
12. Сохрани `.blend`.
13. Переходи к разделу 4: переносу weights.

### 3.11. Рекомендуемые имена объектов

Используй понятные имена:

```text
CL_Torso_Hoodie_01
CL_Legs_Pants_01
CL_Feet_Boots_01
CL_FullOutfit_Combat_01
```

Не используй имена вроде:

```text
Cube
Cube.001
Body copy
New Mesh
```

Потом в Unity будет сложно понять, что это.

## 4. Перенеси веса

Одежде нужны skin weights от базового персонажа.

В Blender:

1. Выбери mesh одежды.
2. Потом с Shift выбери base body mesh.
3. Перейди в `Weight Paint`.
4. Используй:

```text
Weights -> Transfer Weights
```

Рекомендуемые настройки:

```text
Source Layers: By Name
Destination Layers: All Layers
Vertex Mapping: Nearest Face Interpolated
```

После переноса у mesh одежды должны появиться vertex groups с именами костей персонажа.

Проверь это здесь:

```text
Object Data Properties -> Vertex Groups
```

Ты должен видеть bone names исходного персонажа.

## 5. Добавь Armature Modifier

Для каждого mesh одежды:

1. Выбери объект одежды.
2. Добавь modifier `Armature`.
3. В поле `Object` выбери armature импортированного персонажа.
4. Включи `Preserve Volume`, если с ним deformation выглядит лучше.

Modifier stack должен выглядеть примерно так:

```text
Armature
```

Если используешь `Shrinkwrap` или `Solidify`, лучше применить или финализировать их перед экспортом, если ты не хочешь, чтобы Blender рассчитывал их при экспорте.

## 6. Проверь deformation в Blender

Перед экспортом проверь позы:

1. Выбери armature.
2. Перейди в `Pose Mode`.
3. Поворачивай:
   - upper arms
   - forearms
   - thighs
   - calves
   - spine/chest

Проверь:

- hoodie не рвётся на shoulders
- pants не схлопываются на knees
- одежда не отстаёт от костей
- нет участков без weights
- тело не сильно пробивает одежду

Если одежда не следует за позой, значит проблема в weights или Armature modifier.

## 7. Реши проблему clipping тела

Есть два нормальных способа.

### Вариант A: полностью скрыть базовое тело

Для full outfit можно скрыть оригинальный body renderer в Unity и показывать только outfit.

Это самый простой и надёжный вариант для remote игроков.

### Вариант B: скрывать части тела

Для модульной одежды лучше разделить тело на части:

```text
Body_Head
Body_Torso
Body_Arms
Body_Legs
Body_Feet
```

Потом скрывать закрытые части:

```text
Hoodie equipped -> hide Body_Torso and maybe upper arms
Pants equipped -> hide Body_Legs
Boots equipped -> hide Body_Feet
```

Так тело не будет пробиваться сквозь одежду.

Если базовый персонаж сейчас одним mesh, есть два варианта:

- разделить тело в Blender на несколько mesh частей
- или делать outfit meshes, которые полностью закрывают тело и имеют достаточную толщину

## 8. Экспорт из Blender

Выбери:

- clothing mesh
- armature, если нужно

Потом:

```text
File -> Export -> FBX
```

Рекомендуемые настройки:

```text
Selected Objects: ON
Apply Transform: OFF
Forward: -Z Forward
Up: Y Up
Add Leaf Bones: OFF
Only Deform Bones: ON
Bake Animation: OFF
Apply Unit: ON
```

Не экспортируй animations для одежды.

Примеры путей экспорта:

```text
Assets/Characters/Clothing/Outfits/Outfit_Combat_01.fbx
Assets/Characters/Clothing/Torso/Torso_Hoodie_01.fbx
Assets/Characters/Clothing/Legs/Legs_Pants_01.fbx
```

## 9. Import settings в Unity

После импорта clothing FBX:

1. Выбери FBX в Unity.
2. Открой вкладку `Rig`.

Настройки:

```text
Animation Type: Generic
Avatar Definition: Create From This Model
```

Для отдельных частей одежды сам avatar не так важен, если потом mesh будет перепривязан к bones внутри `SyntyVisual` кодом. Но FBX обязан сохранить bone names и skinned mesh.

Вкладка `Model`:

```text
Scale Factor: 1
Read/Write: optional
Import BlendShapes: ON if needed
Import Visibility: ON
```

Materials:

```text
Use External Materials or Extract Materials
```

Создай prefab из импортированного объекта:

```text
Assets/Prefabs/Clothing/Outfits/Outfit_Combat_01.prefab
Assets/Prefabs/Clothing/Torso/Torso_Hoodie_01.prefab
Assets/Prefabs/Clothing/Legs/Legs_Pants_01.prefab
```

## 10. Как это должно работать в Unity-коде

Runtime clothing applier должен:

1. Найти:

```text
ThirdPersonBody/SyntyVisual
```

2. Создать:

```text
SyntyVisual/OutfitRoot
```

3. Инстансить clothing prefab под `OutfitRoot`.

4. Для каждого `SkinnedMeshRenderer` перепривязать bones:

```text
clothingRenderer.bones[i] = FindBoneInHierarchy(syntyVisual, sourceBone.name)
```

5. Назначить:

```text
clothingRenderer.rootBone = matching root bone under SyntyVisual
```

6. Скрыть базовый body renderer или отдельные части тела.

## 11. Рекомендуемая data model

Позже лучше использовать `ScriptableObject`.

Пример:

```csharp
public enum ClothingSlot
{
    FullOutfit,
    Torso,
    Legs,
    Feet,
    Hands,
    Head,
    Back
}
```

```csharp
[CreateAssetMenu(menuName = "Shooter Prototype/Clothing Item")]
public sealed class ClothingItemDefinition : ScriptableObject
{
    public string itemId;
    public ClothingSlot slot;
    public GameObject prefab;
    public bool hideBaseBody;
}
```

Для первой реализации лучше держать всё просто:

```text
Remote default outfit = one full outfit prefab
Hide base body = true
```

## 12. Рекомендуемая первая реализация

Начни с одного цельного outfit:

```text
Outfit_Remote_Default_01
```

В Blender:

1. Импортируй `Ch18_nonPBR.fbx`.
2. Создай full-body outfit mesh.
3. Перенеси weights с base body.
4. Экспортируй FBX.
5. Сделай prefab.

В Unity:

1. Добавь runtime applier для remote.
2. Инстансь outfit под `SyntyVisual`.
3. Перепривяжи bones по именам.
4. Скрой оригинальный body renderer.
5. Оставь skeleton, animator, IK и hitboxes без изменений.

Так получится самый чистый результат: skeleton остаётся тем же, а меняется только визуальная оболочка.

## 13. Почему Genies выглядел плохо

Genies clothing, скорее всего, сделан под другой avatar:

- другие пропорции
- другой bind pose
- другие bone names или hierarchy
- другая форма тела

Даже если Unity может заинстансить такой prefab, он не будет чисто подходить к `Ch18_nonPBR`.

Чтобы использовать Genies assets нормально:

1. Импортируй Genies clothing в Blender.
2. Импортируй `Ch18_nonPBR.fbx`.
3. Подгони форму Genies clothing вокруг `Ch18_nonPBR`.
4. Удали Genies armature/weights.
5. Перенеси weights с `Ch18_nonPBR`.
6. Экспортируй как новую одежду именно для этого проекта.

Genies лучше использовать как source art/reference, а не как plug-and-play одежду.

## 14. Checklist качества

Перед тем как принимать одежду:

- Mesh корректно следует за arms и legs.
- Нет сильных разрывов на shoulders.
- Нет сильного схлопывания на knees/crotch.
- Тело не пробивает одежду в idle/run/crouch.
- Weapon pose всё ещё выглядит нормально.
- Remote left-hand IK всё ещё работает.
- Hitboxes остаются на месте, потому что skeleton не меняется.
- Renderer находится под `SyntyVisual/OutfitRoot`.
- Base body скрыт, если outfit полностью закрывает тело.

## 15. Лучший следующий шаг

Сначала сделай один full-body remote outfit.

Не начинай сразу с множества модульных частей. Сначала добейся одного чистого outfit, который:

- сидит на `Ch18_nonPBR`
- нормально деформируется
- скрывает базовое тело
- работает на remote

После этого уже можно дробить систему на модульные слоты.
