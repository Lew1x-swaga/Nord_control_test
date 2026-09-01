# AGENT_PLAN — этап 6 (для ИИ)

Сетка **списка учеников**, не сетка экранов. Потолка Join нет.

```yaml
plan_id: nord-stage-6
stage: 6
depends_on: [1, 2, 3]
blocks: [7, 8, 9]
stage_7_forbidden: true
stage_4_forbidden: true
commit: only_if_user_asked
concurrent_implementers: 1
waves: 3
reviewer_per_wave: 1
final_reviewer: 1
os: windows-x64
dotnet: "8.0"
ui: wpf
lang_ui: ru
protocol_v: 1
ui_settings_file: "teacher-ui.json"
min_card_width_px: 168
```

## 0. Кто ты и какие субагенты

- **Controller:** этот файл + `docs/subagents.md` + skill `executing-nord-stage6`. Код сам не пишешь, пока человек не сказал «пиши здесь».
- **Implementer:** ровно один `.cursor/agents/nord-stage6-implementer.md` на волну.
- **Reviewer:** после `DONE_WHEN` волны — `.cursor/agents/nord-stage6-reviewer.md`.
- **Fixer:** если Quality = Changes required — снова **тот же** implementer на ту же волну. Не параллелить.
- **Final:** после волны 3 — `.cursor/agents/nord-stage6-final-reviewer.md`. Затем стоп. Не диспатчить stage 7.

Не вызывать `nord-stage1/2/3-*`. Не вызывать stage 7–9, 4–5.

## 1. READ (этот порядок)

1. Этот файл (§0–3 и своя волна)
2. `docs/subagents.md`
3. `docs/requirements.md` — FR-60…64
4. `docs/ui.md` — учитель, вид списка
5. `docs/invariants.md` — FR-20, один HD
6. `src/NordControl.Teacher/MainWindow.xaml` — `StudentsListBox`
7. `src/NordControl.Core/Policies/TeacherPreset.cs` — образец локального JSON (не смешивать пресет приложений с UI)

Не читать чат как спеку.

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| Переключатель лента/сетка только в Teacher WPF | Лимит 24, `join_reject` из‑за числа учеников |
| Карточка: имя, статус, hostname | JPEG / скриншот на карточке (это этап 9) |
| Тот же `SelectStudentAsync` | Второй `stream_start` |
| Локальный `teacher-ui.json` | Поле в протоколе, облако |
| TDD для Core-хелперов | Менять Protocol / ClassClient / захват |
| `dotnet test` зелёный | Коммит без просьбы человека |

`DONE_WHEN` ложен → волна не закончена.

## 3. Волны

| Wave | Tasks | Название | DONE_WHEN (exit 0) |
|---|---|---|---|
| 1 | 26 | `TeacherUiSettings` + расчёт колонок сетки | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~TeacherUiSettingsTests"` |
| 2 | 27 | WPF: лента/сетка, тот же выбор ученика | `dotnet build src/NordControl.Teacher/NordControl.Teacher.csproj` |
| 3 | 28 | Регрессия ядра | `dotnet test tests/NordControl.Tests` и `dotnet build NordControl.sln` |

## 4. Карта файлов

```text
src/NordControl.Core/TeacherUiSettings.cs
  enum StudentListLayout { List, Grid }
  TeacherUiSettings { StudentListLayout Layout }
  TeacherUiSettingsManager Load/Save → %LocalAppData%\NordControl\teacher-ui.json
  StudentGridLayout.ColumnCount(double panelWidth, int minCardWidth = 168) → max(1, floor(width/min))

src/NordControl.Teacher/MainWindow.xaml
  кнопки/Toggle «Лента» / «Сетка» над StudentsListBox
  при Grid: ItemsPanel UniformGrid, колонки из StudentGridLayout
  шаблон карточки = те же Binding (DisplayName, Hostname, Status*)

src/NordControl.Teacher/MainWindow.xaml.cs
  загрузка/сохранение Layout; смена панели без пересоздания ItemsSource
  SelectionChanged без изменений смысла (FR-20)

tests/NordControl.Tests/TeacherUiSettingsTests.cs
```

Не трогать: `ProtocolConstants` (кроме если случайно), `FrameCodec`, `ClassHub.SelectStudentAsync`, Student capture.

---

## Wave 1 / Task 26: настройки UI и колонки

- **DEPENDS_ON:** этапы 1–3
- **WAVE:** 1
- **SKILL:** test-driven-development
- **DONE_WHEN:** filter `TeacherUiSettingsTests` PASS
- **Files:** `TeacherUiSettings.cs`, `TeacherUiSettingsTests.cs`

Поведение:

- Файл отсутствует / битый JSON → `Layout = List`, не бросать.
- Save создаёт каталог NordControl.
- `ColumnCount(0)` и отрицательная ширина → 1. `ColumnCount(500, 168)` → 2. `ColumnCount(168, 168)` → 1.
- JSON snake_case, как пресет. Ключ `student_list_layout`: `list` | `grid`.
- Не писать quick apps в этот файл.

- [ ] Падающий тест на Load/Save во временный путь
- [ ] Падающий тест на ColumnCount
- [ ] Минимальная реализация
- [ ] `DONE_WHEN`

## Wave 2 / Task 27: WPF

- **DEPENDS_ON:** Wave 1
- **WAVE:** 2
- **SKILL:** нет TDD на XAML (WPF не в test tfm)
- **DONE_WHEN:** `dotnet build` Teacher PASS
- **Files:** `MainWindow.xaml`, `MainWindow.xaml.cs`

Поведение:

- Строки: «Лента», «Сетка» (русский).
- Сетка уменьшает вертикальный скролл: несколько карточек в ряд на ширине левой колонки (~280–360 px → обычно 1–2 колонки; если панель шире — больше).
- Переключение на живом классе не вызывает `StopClass` / `session_end`.
- Выбор ученика в сетке вызывает существующий `StudentsListBox_SelectionChanged` / `SelectStudentAsync`.
- Сохранение Layout при смене и при закрытии окна.

- [ ] Toggle + ItemsPanel
- [ ] Load при старте окна
- [ ] Build Teacher

## Wave 3 / Task 28: регрессия

- **DEPENDS_ON:** Wave 2
- **WAVE:** 3
- **DONE_WHEN:** полный `dotnet test` + `dotnet build NordControl.sln`

- [ ] Нет новых JSON `type` в Protocol
- [ ] `rg` по Teacher: нет захвата/Image в шаблоне ученика
- [ ] Все старые тесты зелёные

## Приёмка человеком

Много учеников в сетке без длинного скролла (насколько позволяет ширина панели). Лента↔сетка. Один большой JPEG у выбранного.
