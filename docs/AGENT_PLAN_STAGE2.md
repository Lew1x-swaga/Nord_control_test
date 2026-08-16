# AGENT_PLAN — этап 2 (для ИИ)

```yaml
plan_id: nord-stage-2
stage: 2
stage_3_forbidden: true
commit: only_if_user_asked
concurrent_implementers: 1
waves: 4
reviewer_per_wave: 1
final_reviewer: 1
os: windows-x64
dotnet: "8.0"
ui: wpf
lang_ui: ru
stream_fps_min: 8
stream_fps_max: 12
max_dimension: 1280
jpeg_quality: 70
process_interval_ms: 2500
process_max_items: 40
protocol_v: 1
```

## 0. Кто ты

- **Controller:** читаешь этот файл и `docs/subagents.md`. Диспатчишь **один** `.cursor/agents/nord-stage2-implementer.md` на волну. Не пишешь код сам, если идёт SDD.
- **Implementer:** субагент `nord-stage2-implementer`. Читаешь **только свою волну** + §1–3.
- **Reviewer:** субагент `nord-stage2-reviewer`. После всех волн — `nord-stage2-final-reviewer`.

## 1. READ (строго этот порядок)

1. Этот файл (§0–3 и своя волна)
2. `docs/subagents.md`
3. `docs/requirements.md` — таблица «Этап 2» (FR-20…23)
4. `docs/protocol.md` — бинарный JPEG кадр и JSON process_list
5. `docs/invariants.md` — инварианты (один видеопоток, видимый надзор, fail-open)
6. `docs/ui.md`

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| DXGI Desktop Duplication / GDI fallback на Windows (Student) | DXGI / Win32 типы внутри `NordControl.Protocol` |
| Сжатие в JPEG (длинная сторона $\le 1280$, 8–12 fps) | Стриминг более 1 ученика одновременно |
| Бинарный кадр TCP: type 2 (`width:uint32`, `height:uint32`, `ts_ms:uint64`, jpeg_bytes) | JSON/base64 для передачи видеокадров |
| Сигналы `stream_start` / `stream_stop` (только выбранному) | Стриминг при `StreamPaused` (stale $\ge 10$ с) или не в `Online` |
| Список процессов: только с главным окном + активный exe (до ~40 шт., раз в 2.5 с) | Сплошной дамп процессов без окон (`svchost`, фоновые службы) |
| Переключение выбранного ученика: предыдущему `stream_stop`, новому `stream_start` | Кейлоггеры, скрытие процесса, запись экрана на диск |
| UI учителя: отображение JPEG-кадра + таблица процессов справа | Этап 3: `launch_app`, `set_block_list`, AppLocker, WFP, kill |
| `dotnet test` зеленый в конце волны | Коммит без явной просьбы человека |

## 3. Волны

| Wave | Tasks | Название | DONE_WHEN (exit 0) |
|---|---|---|---|
| 1 | 10, 11 | Протокол этапа 2 (JpegFrame + WireMessage DTO) и тесты | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~JpegFrameCodecTests\|FullyQualifiedName~ProcessListMessageTests"` |
| 2 | 12, 13 | Захват экрана (DXGI/GDI) и сборщик процессов ученика (Student) | `dotnet build src/NordControl.Student` и `dotnet test tests/NordControl.Tests` |
| 3 | 14, 15 | Интеграция стриминга и процессов в ClassHub и ClassClient (Core) | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~ScreenStreamTests\|FullyQualifiedName~ProcessListTests"` |
| 4 | 16, 17 | UI Учителя (просмотр экрана + таблица процессов) и сквозные тесты | `dotnet test tests/NordControl.Tests` и `dotnet build NordControl.sln` |

## 4. Карта новых и обновляемых файлов

```text
src/NordControl.Protocol/
  JpegFrame.cs                    # Бинарный заголовок: Width (uint32), Height (uint32), TimestampMs (uint64), Payload
  WireMessage.cs                  # Добавление DTO: ProcessListMessage, ProcessItemInfo, stream_start, stream_stop

src/NordControl.Core/
  ScreenStreamHub.cs              # Управление потоком на стороне учителя (выбранный ученик, маршрутизация кадров)
  ClassHub.cs                     # Обработка stream_start/stream_stop, прием Type 2 (JPEG) и process_list
  ClassClient.cs                  # Прием команд stream_start/stream_stop, таймер отправки процессов

src/NordControl.Student/
  Capture/IScreenCapturer.cs       # Интерфейс захватчика экрана
  Capture/DxgiScreenCapturer.cs    # DXGI Desktop Duplication + JPEG кодирование
  Capture/GdiScreenCapturer.cs     # GDI/Graphics fallback захват экрана
  Services/ProcessMonitor.cs       # Сбор оконных процессов и активного окна через Win32/Process

src/NordControl.Teacher/
  MainWindow.xaml                 # Правая панель: Image для JPEG + DataGrid/ListView процессов
  MainWindow.xaml.cs              # Подписка на кадры и обновление списка процессов выбранного ученика

tests/NordControl.Tests/
  JpegFrameCodecTests.cs          # Тесты бинарной сериализации/десериализации JpegFrame
  ProcessListMessageTests.cs      # Тесты JSON сериализации process_list
  ScreenStreamTests.cs            # Тесты управления активным потоком (1 поток, switch, stop)
  ProcessListTests.cs             # Тесты фильтрации и периодичности process_list
```

---

## Wave 1 / Task 10, 11: Протокол этапа 2 (JpegFrame и WireMessage DTO)

- **DEPENDS_ON:** Stage 1
- **WAVE:** 1
- **SKILL:** test-driven-development
- **DONE_WHEN:** `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~JpegFrameCodecTests|FullyQualifiedName~ProcessListMessageTests"` PASS
- **Files:** `src/NordControl.Protocol/JpegFrame.cs`, `src/NordControl.Protocol/WireMessage.cs`, тесты

**Спецификация бинарного кадра Type 2 (JPEG):**
```text
uint32 width        (big-endian, 4 байта)
uint32 height       (big-endian, 4 байта)
uint64 timestamp_ms (big-endian, 8 байт)
byte[] jpeg_data    (оставшаяся часть payload кадра)
```

**Спецификация сообщений JSON:**
- `stream_start`: `{"v":1,"type":"stream_start"}`
- `stream_stop`: `{"v":1,"type":"stream_stop"}`
- `process_list`:
  ```json
  {
    "v": 1,
    "type": "process_list",
    "active_exe": "chrome.exe",
    "items": [
      { "exe": "chrome.exe", "pid": 1234, "title": "Вкладка — Браузер" }
    ]
  }
  ```

---

## Wave 2 / Task 12, 13: Захват экрана и мониторинг процессов (Student)

- **DEPENDS_ON:** Wave 1
- **WAVE:** 2
- **SKILL:** test-driven-development
- **DONE_WHEN:** `dotnet build src/NordControl.Student` PASS и `dotnet test tests/NordControl.Tests` PASS
- **Files:** `src/NordControl.Student/Capture/*`, `src/NordControl.Student/Services/ProcessMonitor.cs`

**Поведение:**
- `IScreenCapturer`: `Task<JpegFrame?> CaptureFrameAsync(int maxDimension, int quality, CancellationToken ct)`
- `DxgiScreenCapturer` (Windows Desktop Duplication API) с безопасным `GdiScreenCapturer` fallback при смене рабочего стола/UAC.
- Масштабирование: если длинная сторона $> 1280$, пропорционально уменьшать.
- `ProcessMonitor`: перечисление процессов, имеющих `MainWindowHandle != 0` и непустой заголовок, плюс `GetForegroundWindow` для определения `active_exe`. Лимит: не более 40 элементов, без системных фоновых служб.

---

## Wave 3 / Task 14, 15: Маршрутизация стрима и процессов в Core

- **DEPENDS_ON:** Wave 1, Wave 2
- **WAVE:** 3
- **SKILL:** test-driven-development
- **DONE_WHEN:** `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~ScreenStreamTests|FullyQualifiedName~ProcessListTests"` PASS
- **Files:** `src/NordControl.Core/ClassHub.cs`, `src/NordControl.Core/ClassClient.cs`, `src/NordControl.Core/StudentSession.cs`, тесты

**Поведение:**
- `ClassHub.SelectStudent(string? studentId)`:
  - Если был выбран другой ученик $\rightarrow$ отправить ему `stream_stop`.
  - Новому выбранному ученику $\rightarrow$ отправить `stream_start`.
  - При отключении/reconnect выбранного ученика $\rightarrow$ событие очистки экрана.
- `ClassHub.ScreenFrameReceived`: событие `(string studentId, JpegFrame frame)` для UI.
- `ClassHub.ProcessListReceived`: событие `(string studentId, ProcessListMessage message)` для UI.
- `ClassClient`:
  - При получении `stream_start` $\rightarrow$ запуск цикла захвата (8–12 fps, пауза если `StreamPaused`).
  - При получении `stream_stop` или `StreamPaused` $\rightarrow$ остановка отправки кадров.
  - Фоновая отправка `process_list` раз в 2.5 с, только когда `Session.Status == Online`.

---

## Wave 4 / Task 16, 17: UI Учителя и сквозная интеграция

- **DEPENDS_ON:** Wave 3
- **WAVE:** 4
- **SKILL:** verification-before-completion
- **DONE_WHEN:** `dotnet test tests/NordControl.Tests` PASS и `dotnet build NordControl.sln` PASS
- **Files:** `src/NordControl.Teacher/MainWindow.xaml`, `MainWindow.xaml.cs`, `tests/NordControl.Tests/LoopbackStage2Tests.cs`

**Поведение UI Учителя:**
- При клике на ученика в списке $\rightarrow$ вызов `hub.SelectStudent(student.Id)`.
- Правая часть:
  - Компонент `Image` для рендеринга входящих кадров (через `BitmapSource` в UI потоке).
  - Таблица активных процессов выбранного ученика (Колонка: Иконка/Статус активности, Имя EXE, PID, Заголовок окна).
  - Если ученик не выбран / отключился $\rightarrow$ отображение заглушки «Выберите ученика из списка».
- Интеграционный тест: `LoopbackStage2Tests` проверяет полный цикл запуска стрима, передачу бинарного кадра Type 2 и получение списка процессов на loopback.

---

## Инварианты Этапа 2

1. **Один видеопоток:** В любой момент времени в сети передается максимум 1 поток JPEG.
2. **Только при Online и не Stale:** При `silent >= 10s` (`StreamPaused`) захват немедленно приостанавливается.
3. **LAN-only:** Никаких облачных кодеков, WebRTC-серверов или внешних вызовов.
4. **Видимость:** Захват не скрывает себя от системы, не перехватывает ввод мыши/клавиатуры.
5. **Изоляция:** В `NordControl.Protocol` нет зависимостей от `System.Drawing`, `WPF` или `Win32`.
