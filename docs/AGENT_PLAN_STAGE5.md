# AGENT_PLAN — этап 5 (для ИИ)

Школа: автозапуск агента **без** запретов до сессии учителя. **Не начинать** без явной просьбы («служба», «автозапуск», «тихая установка», «памятка школе»).

```yaml
plan_id: nord-stage-5
stage: 5
depends_on: [4]
requires_user_request: true
commit: only_if_user_asked
concurrent_implementers: 1
waves: 3
reviewer_per_wave: 1
final_reviewer: 1
os: windows-x64
dotnet: "8.0"
```

## 0. Субагенты

- **Controller:** `executing-nord-stage5`. Нет просьбы человека → стоп. Этап 4 желателен (тихая установка тем же Setup), не смешивать волны 4 и 5 в одном implementer-запуске.
- **Implementer:** `.cursor/agents/nord-stage5-implementer.md`
- **Reviewer:** `.cursor/agents/nord-stage5-reviewer.md`
- **Fixer:** тот же implementer
- **Final:** `.cursor/agents/nord-stage5-final-reviewer.md`

Не лицензии, не сайт, не облако, не чат, не этап 9 «заодно».

## 1. READ

1. Этот файл + волна
2. `docs/subagents.md`
3. `docs/requirements.md` — FR-50…54
4. `docs/invariants.md` — fail-open, нет автозапуска **до** этого этапа; после — автозапуск без политик
5. `docs/architecture.md` — служба = тот же Core
6. `docs/distribution.md`
7. `src/NordControl.Core/StudentSession.cs` — Idle без политик
8. `src/NordControl.Core/Policies/RamAppBlocker.cs` — Clear на Idle/Ended

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| После reboot агент ждёт класс (Idle) | Блоклист до Join |
| Fail-open если учителя нет | Сторожа, прятки из диспетчера |
| Тихие ключи Setup | HTTP/лицензия |
| Памятка в `docs/` | AppLocker, драйвер |
| Тот же `ClassClient` | Второй протокол |

## 3. Волны

| Wave | Tasks | Название | DONE_WHEN |
|---|---|---|---|
| 1 | 44 | Режим «агент после reboot» в Core/Student: Idle, blocker empty | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~StudentSessionTests|FullyQualifiedName~AppBlockerTests"` + новые тесты `Stage5AutostartTests` если добавлены |
| 2 | 45 | Служба или HKCU Run — один способ, документировать | build Student (+ installer ключ если этап 4 есть) |
| 3 | 46 | `docs/SCHOOL.md` памятка + тихая установка | test+sln; SCHOOL.md существует |

## 4. Карта файлов

```text
src/NordControl.Student/     # старт без UI-паники; плашка «ожидание класса»
docs/SCHOOL.md               # LAN, firewall, PIN, reboot, fail-open
installer/                   # ключ /quiet, компонент автозапуска ученика
tests/NordControl.Tests/Stage5AutostartTests.cs  # без сессии ShouldHoldPolicies == false
```

Служба Session 0 **не захватывает экран**. Если DXGI из службы невозможен — автозапуск в user session (папка Автозагрузка / Run ключ текущего ученика), не маскировать процесс. Явно выбрать **user logon autostart**, не kernel service, если захват иначе мёртв. Зафиксировать выбор в SCHOOL.md.

---

## Wave 1 / Task 44

TDD: нет класса → нет blocklist. Не менять 10 с / 120 с.

## Wave 2 / Task 45

Видимый процесс `NordControl.Student`. Не прятать.

## Wave 3 / Task 46

Памятка: одна LAN, отключить client isolation, разрешить firewall Teacher, PIN на доске, reboot снимает запреты пока нет Join.

## Приёмка человеком

Reboot ученика → агент ждёт. Запреты только после Join. Убить Teacher → fail-open как этап 1.
