# AGENT_PLAN — этап 8 (для ИИ)

Одностороннее уведомление учителя. Не чат. Не автотост.

```yaml
plan_id: nord-stage-8
stage: 8
depends_on: [7]
blocks: [9]
stage_9_forbidden: true
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
max_teacher_message_chars: 400
```

## 0. Субагенты

- **Controller:** `executing-nord-stage8`. Этап 7 должен быть готов.
- **Implementer:** `.cursor/agents/nord-stage8-implementer.md`
- **Reviewer:** `.cursor/agents/nord-stage8-reviewer.md`
- **Fixer:** тот же implementer, та же волна
- **Final:** `.cursor/agents/nord-stage8-final-reviewer.md`. Не stage 9.

Не диспатчить 1–3, 6–7 повтор, 9, 4–5. Не вызывать preview type 3.

## 1. READ

1. Этот файл + волна
2. `docs/subagents.md`
3. `docs/requirements.md` — FR-80…84
4. `docs/protocol.md` — `teacher_message`
5. `docs/ui.md` — уведомление ученика, поле учителя
6. `docs/invariants.md`
7. `src/NordControl.Protocol/WireMessage.cs` — поле `Message` уже есть; добавить `MessageId`
8. `src/NordControl.Core/ClassHub.cs` — образец Send/Broadcast + группы этапа 7
9. `src/NordControl.Student/ToastWindow.xaml` — **не копировать** автоскрытие и розовую рамку
10. `src/NordControl.Student/Services/SoundNotification.cs`

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| JSON `teacher_message` + `message_id` + `message` | Ответ ученика, лента чата, стопка окон |
| Обрезать >400 символов на хабе | Рвать сокет из‑за длины |
| Уведомление висит до крестика | `DispatcherTimer` авто-Close как у Toast |
| Центр, чуть ниже верха; свои цвета | Правый нижний угол; копия ToastWindow |
| Крестик ≠ LeaveClass / Exit | Лок ввода, «нажми OK» блокирует ПК |
| Лог: факт и Length, не текст | Писать message в файл |
| `session_end` закрывает UI | Догоняющая очередь после Ended |
| Адресаты: выбранный / группа / все | Превью JPEG |

Константы только в `ProtocolConstants` (`MaxTeacherMessageChars = 400`).

## 3. Волны

| Wave | Tasks | Название | DONE_WHEN (exit 0) |
|---|---|---|---|
| 1 | 33 | DTO + константа + сериализация | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~TeacherMessageProtocolTests"` |
| 2 | 34 | Hub/Client + группа + fail-open событие | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~TeacherMessageProtocolTests|FullyQualifiedName~LoopbackStage8Tests"` |
| 3 | 35 | Окно уведомления ученика | `dotnet build src/NordControl.Student/NordControl.Student.csproj` |
| 4 | 36 | UI учителя + регрессия | `dotnet test tests/NordControl.Tests` и `dotnet build NordControl.sln` |

## 4. Карта файлов

```text
src/NordControl.Protocol/ProtocolConstants.cs   MaxTeacherMessageChars
src/NordControl.Protocol/WireMessage.cs         MessageId

src/NordControl.Core/ClassHub.cs
  Truncate + SendTeacherMessageAsync(studentId, text)
  BroadcastTeacherMessageAsync(text)
  SendTeacherMessageToGroupAsync(groupId, text)
  message_id = Guid на каждое отправление (одному ученику свой id ок)

src/NordControl.Core/ClassClient.cs
  type teacher_message → событие TeacherMessageReceived(string id, string text)
  session_end / Ended → событие TeacherMessageDismissed  (или тот же существующий переход; UI закрывает)

src/NordControl.Student/TeacherNoticeWindow.xaml(.cs)
  Topmost, без авто-таймера, кнопка-крестик
  позиция: WorkArea центр X, Top = WorkArea.Top + ~96
  цвета: не #F43F5E тоста запрета (например синий/янтарный акцент из Theme.xaml)
  одно окно: повтор Replace text; не коллекция

src/NordControl.Teacher/MainWindow.xaml(.cs)
  TextBox + «Отправить выбранному» / «группе» / «всем»

tests/NordControl.Tests/TeacherMessageProtocolTests.cs
tests/NordControl.Tests/LoopbackStage8Tests.cs
```

---

## Wave 1 / Task 33: протокол

- **SKILL:** test-driven-development
- JSON roundtrip snake_case `message_id`, `type=teacher_message`.
- Константа 400. Тест: хаб-хелпер truncate можно в Protocol статическим `WireMessage.ClipTeacherMessage` **или** только в Core — тогда волна 1 тестирует сериализацию, truncate во волне 2. Предпочтение: `ProtocolConstants.MaxTeacherMessageChars` в волне 1; clip в Core волна 2.

Не добавлять type 3.

## Wave 2 / Task 34: Core

Loopback два клиента:

- Broadcast → оба получили текст через событие клиента (подписка на ClassClient; если события нет — добавить `event Action<string,string>? TeacherMessageReceived`).
- Group A only → клиент B не получил.
- StopClass → клиент переходит Ended; тест проверяет, что после этого новое сообщение не применяется (хаб не running).

Пустой/whitespace текст не слать (false).

Логи Core: не интерполировать полный message.

## Wave 3 / Task 35: UI ученика

Не использовать `ToastWindow.ShowToast` для этого сценария.

Звук: `SoundNotification.PlayDing` один раз при показе/замене.

Крестик: `Close()`, сессия жива. Не вызывать leave class.

При `TeacherMessageDismissed` / уходе в Idle — Close если открыто.

## Wave 4 / Task 36: UI учителя + регрессия

Поле ограничить 400 в UI. Пустое — не слать.

Полный test + sln. Старые тосты блоклиста автоскрываются как раньше.

## Приёмка человеком

Сообщение всем висит, пока не крестик; группе А — у Б нет; ученик ответить не может.
