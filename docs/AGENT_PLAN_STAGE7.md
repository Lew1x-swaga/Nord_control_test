# AGENT_PLAN — этап 7 (для ИИ)

Группы = именованные подмножества `student_id` в RAM хаба. По проводу — те же `launch_app` / `set_block_list`.

```yaml
plan_id: nord-stage-7
stage: 7
depends_on: [6]
blocks: [8, 9]
stage_6_must_be_done: true
stage_8_forbidden: true
stage_4_forbidden: true
commit: only_if_user_asked
concurrent_implementers: 1
waves: 4
reviewer_per_wave: 1
final_reviewer: 1
os: windows-x64
dotnet: "8.0"
ui: wpf
lang_ui: ru
protocol_v: 1
```

## 0. Кто ты и какие субагенты

- **Controller:** skill `executing-nord-stage7`. Не стартовать, пока этап 6 не принят (final-reviewer READY **или** человек сказал «делай 7»).
- **Implementer:** `.cursor/agents/nord-stage7-implementer.md` — одна волна, один писатель.
- **Reviewer:** `.cursor/agents/nord-stage7-reviewer.md` после `DONE_WHEN`.
- **Fixer:** тот же implementer, та же волна.
- **Final:** `.cursor/agents/nord-stage7-final-reviewer.md`. Стоп. Не stage 8.

Не диспатчить stage 1–3, 6 (повтор), 8–9, 4–5.

## 1. READ

1. Этот файл (§0–3 и волна)
2. `docs/subagents.md`
3. `docs/requirements.md` — FR-70…74
4. `docs/protocol.md` — группы **не** новый JSON type
5. `docs/ui.md` — группы учителя
6. `docs/invariants.md` — fail-open, RAM
7. `src/NordControl.Core/ClassHub.cs` — `SendLaunchAppAsync`, `BroadcastLaunchAppAsync`, `SendBlockListAsync`, `StopClassAsync`
8. `src/NordControl.Teacher/MainWindow.xaml.cs` — кнопки выбранный/всем

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| `Dictionary` групп в `ClassHub`, RAM | Файл журнала класса, облако |
| 0 или 1 группа на ученика | Ученик в двух группах |
| Адресация launch/block: id / groupId / all | Новый wire `type` для членства |
| `StopClassAsync` чистит группы | Отдельный PIN на группу |
| Смена группы без disconnect | Раздача файлов, чат группы |
| TDD Core/loopback | Коммит без просьбы |

## 3. Волны

| Wave | Tasks | Название | DONE_WHEN (exit 0) |
|---|---|---|---|
| 1 | 29 | Модель групп в `ClassHub` | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~ClassGroupTests"` |
| 2 | 30 | Launch/block группе + fail-open | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~ClassGroupTests|FullyQualifiedName~LoopbackStage7Tests"` |
| 3 | 31 | UI учителя | `dotnet build src/NordControl.Teacher/NordControl.Teacher.csproj` |
| 4 | 32 | Регрессия | `dotnet test tests/NordControl.Tests` и `dotnet build NordControl.sln` |

## 4. Карта файлов

```text
src/NordControl.Core/ClassHub.cs
  record/class ClassGroup(string Id, string Name)
  CreateGroup(name) → id (непустой trim, имя уникально в классе без учёта регистра? лучше уникальность не требовать — id хватит)
  RenameGroup(id, name)
  DisbandGroup(id) → ученики становятся без группы
  SetStudentGroup(studentId, groupId | null)
  GetStudentGroupId(studentId)
  Groups { get }
  IReadOnlyList<string> GetOnlineStudentIdsInGroup(groupId)
  SendLaunchAppToGroupAsync / SendBlockListToGroupAsync / SendLaunchAppAfterBlockListToGroupAsync
  StopClassAsync / session_end: ClearGroups()

src/NordControl.Teacher/MainWindow.xaml(.cs)
  список групп, «Новая группа», переименовать, распустить
  «В группу» для выбранных в списке учеников
  метка группы на строке/карточке (этап 6)
  кнопки запуска и блоклиста «Группе»

tests/NordControl.Tests/ClassGroupTests.cs
tests/NordControl.Tests/LoopbackStage7Tests.cs
```

`ConnectedStudent` можно не ломать: группа в отдельном `ConcurrentDictionary<string,string>` studentId→groupId. Ушедший ученик — вычистить ключ.

---

## Wave 1 / Task 29: RAM-группы

- **SKILL:** test-driven-development
- **DONE_WHEN:** `ClassGroupTests` PASS

Поведение:

- Класс не запущен / нет группы → SetStudentGroup false.
- Disband: GetStudentGroupId → null.
- Студент только в последней назначенной группе.
- CreateGroup после StopClass — либо throw, либо no-op false; после Stop словарь пуст.
- Имя пустое — не создавать.

- [ ] RED тесты без TCP
- [ ] Реализация в ClassHub
- [ ] GREEN

Тесты без живых сокетов: вызывать API хаба до StartClass где возможно; для StopClass — короткий Start+Stop.

## Wave 2 / Task 30: адресация команд

- **SKILL:** test-driven-development
- **DONE_WHEN:** `ClassGroupTests` + `LoopbackStage7Tests` PASS

Loopback (как `LoopbackStage3Tests`, порты `TestPorts.NextPair()`):

- Два клиента, PIN верный.
- Группа А = клиент1, группа Б = клиент2.
- `SendLaunchAppToGroupAsync(A, exe)` → FakeAppLauncher только у 1.
- `SendBlockListToGroupAsync(B, [notepad.exe])` → FakeAppBlocker только у 2.
- `BroadcastBlockListAsync` по-прежнему оба.
- `StopClassAsync` → оба blocker.Clear (уже есть) **и** Groups пуст.

Не реализовывать `teacher_message`.

## Wave 3 / Task 31: UI

- **DONE_WHEN:** build Teacher

Строки русские: «Группы», «Новая группа», «Переименовать», «Распустить», «Назначить в группу», «Запустить у группы», «Блоклист группе».

Не ломать лента/сетка этапа 6: метка группы видна в обоих видах.

Диалог конфликта launch/block — тот же, что для выбранного/всех.

## Wave 4 / Task 32: регрессия

Полный test + sln. Нет новых wire `type`. FR-20 жив.

## Приёмка человеком

Две группы: у А открылся учебный exe, у Б браузер в блоклисте; «всем» работает; «Завершить класс» снимает запреты.
