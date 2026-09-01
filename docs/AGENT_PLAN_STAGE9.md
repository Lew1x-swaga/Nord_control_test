# AGENT_PLAN — этап 9 (для ИИ)

Сетка **экранов** (превью type 3). Не сетка списка этапа 6. Один HD type 2.

```yaml
plan_id: nord-stage-9
stage: 9
depends_on: [8]
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
jpeg_preview_type: 3
preview_long_side: 320
preview_interval_ms: 2500
preview_quality: 40
```

## 0. Субагенты

- **Controller:** `executing-nord-stage9`. Нужны этапы 6–8 (6 — чтобы не перепутать UI сеток).
- **Implementer:** `.cursor/agents/nord-stage9-implementer.md`
- **Reviewer:** `.cursor/agents/nord-stage9-reviewer.md`
- **Fixer:** тот же implementer
- **Final:** `.cursor/agents/nord-stage9-final-reviewer.md`. Не этап 4.

Не диспатчить 1–3 повтор, 4–5, H.264, запись на диск, мышь.

## 1. READ

1. Этот файл + волна
2. `docs/subagents.md`
3. `docs/requirements.md` — FR-90…94
4. `docs/protocol.md` — type 3, `preview_enable` / `preview_disable`
5. `docs/invariants.md` §4 (один HD + превью)
6. `docs/ui.md` — сетка экранов ≠ сетка списка
7. `src/NordControl.Protocol/FrameCodec.cs`, `JpegFrame.cs`, `ProtocolConstants.cs`
8. `src/NordControl.Core/ClassHub.cs` — разбор type 2 ~строка 603
9. `src/NordControl.Core/ClassClient.cs` — цикл capture ~681
10. `src/NordControl.Core/StudentSession.cs` — `ShouldCapture`
11. `src/NordControl.Student/Capture/IScreenCapturer.cs` — уже есть `maxDimension`, `quality`
12. `src/NordControl.Teacher/MainWindow.xaml` — вкладка «Мониторинг»

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| Type 3 = JpegFrame, константы из ProtocolConstants | JPEG в JSON/base64 |
| HD только `stream_start` (type 2) | 24×1280×10fps |
| `preview_enable` всем онлайн | Баннер надзора из type 3 |
| Интервал ≥ 2500 мс, сторона ≤ 320 | Писать кадры на диск |
| Клик плитки = `SelectStudentAsync` | Удалённый ввод |
| Заглушка offline/reconnect | Подменить левую сетку учеников этапа 6 |
| Выбранный HD: не слать type 3 | H.264 обязательным |

Неизвестный type байта: не падать процессом; превью просто нет.

## 3. Волны

| Wave | Tasks | Название | DONE_WHEN (exit 0) |
|---|---|---|---|
| 1 | 37 | Константы, type 3 codec, JSON preview_* | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~PreviewProtocolTests"` |
| 2 | 38 | Session + Client + Hub маршрутизация | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~PreviewProtocolTests|FullyQualifiedName~LoopbackStage9Tests"` |
| 3 | 39 | Student callback превью (maxDimension 320) | `dotnet build src/NordControl.Student/NordControl.Student.csproj` |
| 4 | 40 | UI сетки экранов + регрессия | `dotnet test tests/NordControl.Tests` и `dotnet build NordControl.sln` |

## 4. Карта файлов

```text
src/NordControl.Protocol/ProtocolConstants.cs
  JpegPreviewMessageType = 3
  PreviewLongSideMax = 320
  PreviewIntervalMs = 2500
  PreviewJpegQuality = 40

src/NordControl.Protocol/FrameCodec.cs
  WriteJpegPreviewMessageAsync = WriteAsync(type 3, JpegFrame.Encode())

src/NordControl.Core/StudentSession.cs
  PreviewEnabled
  ShouldPreview = Online && PreviewEnabled && !StreamPaused
  (HD ShouldCapture без изменений)

src/NordControl.Core/ClassClient.cs
  preview_enable / preview_disable
  цикл превью: если ShouldPreview && !ShouldCapture → CapturePreviewCallback, Write type 3
  если ShouldCapture (HD) — не слать type 3
  CapturePreviewCallback отдельно или тот же capturer с другими аргументами

src/NordControl.Core/ClassHub.cs
  BroadcastPreviewEnable/Disable
  event PreviewFrameReceived(string studentId, JpegFrame)
  type 3 не путать с ScreenFrameReceived (type 2)

src/NordControl.Student/MainWindow.xaml.cs
  CapturePreviewCallback = capturer.CaptureFrameAsync(320, 40, ct)

src/NordControl.Teacher/MainWindow.xaml(.cs)
  режим «Один экран» / «Сетка экранов» в мониторинге
  ItemsControl плиток: Image из type 3; клик → SelectStudent
  офлайн: плейсхолдер «нет» / «переподключение»
  включение сетки → preview_enable; выключение → preview_disable
  HD Image справа/крупно для выбранного как сейчас

src/NordControl.Student/ScreenWatcherBannerWindow — только при stream_start/ShouldCapture

tests/NordControl.Tests/PreviewProtocolTests.cs
tests/NordControl.Tests/LoopbackStage9Tests.cs
```

---

## Wave 1 / Task 37: протокол

TDD: Encode/Decode type 3 через FrameCodec; JSON preview_enable. Константы не литералы в тестах вне ProtocolConstants (в тестах можно читать константы).

Тесты портов 47830/47831 не обязательны в этой волне.

## Wave 2 / Task 38: Core loopback

- Два клиента. Preview enable. Hub получает type 3 от обоих (фейковый callback отдаёт крошечный JpegFrame).
- Select student1: student1 шлёт type 2 (если callback HD задан) и **не** type 3; student2 продолжает type 3.
- Stale: не обязательно эмулировать 10 с в этом тесте, если дорого; минимум: preview_disable останавливает type 3.
- Кадры не пишутся в Path.GetTempPath в продуктовом коде (тест может собрать bytes в RAM).

## Wave 3 / Task 39: Student

Прокинуть второй callback. Не менять баннер: баннер завязан на надзор HD, не на preview.

Build Student.

## Wave 4 / Task 40: Teacher UI + регрессия

Левая панель этапа 6 не показывает Image ученика.

Полный test + sln. FR-20: один ScreenFrameReceived HD.

## Приёмка человеком

≥8 плиток обновляются на реальном кабинете по возможности; клик даёт большой кадр одному; список учеников этапа 6 без скриншотов.
