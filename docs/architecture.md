# Архитектура

Статус: этапы 1–3 приняты. Планы реализации 6–9 и 4–5 — `docs/AGENT_PLAN_STAGE*.md`.

Стек зафиксирован: **C# / .NET 8 / WPF**. Не WinUI, не Electron.

## Компоненты

```text
NordControl.sln
  src/NordControl.Protocol    кадры TCP, JSON, UDP-строки, константы, JpegFrame
  src/NordControl.Core        стейт-машина, ClassHub, ClassClient, RAM-политики
  src/NordControl.Teacher     только WPF поверх ClassHub
  src/NordControl.Student     WPF/tray, захват DXGI/GDI, баннер надзора, тосты
  tests/NordControl.Tests     xUnit (net8.0), без WPF
```

Хабы в Core, не в WPF: иначе loopback-тесты не смогут сослаться на `net8.0-windows`.

| Модуль | Ответственность |
|---|---|
| Protocol | На проводе. Без WPF и Win32 |
| Core | `StudentSession`, `ClassHub`, `ClassClient`, `RamAppBlocker`, `AppLauncher`, пресет |
| Teacher | UI списка (лента/сетка — этап 6), PIN, JPEG выбранного, процессы, запуск/блоклист, группы (7), сообщение (8), превью экранов (9) |
| Student | Плашка, трей, захват экрана, вотчер процессов, тосты/баннер, уведомление учителя (8) |

Захват DXGI живёт в Student (Windows), в Protocol уходит JPEG как `messageType=2` (HD выбранного). Этап 9: редкий `messageType=3` для сетки превью. Политики только в Core/Student, в RAM. Служба автозапуска — тот же Core (этап 5, по плану).

## Топология

```text
Учитель  UDP 47820  announce (без PIN)
         TCP 47821  сессия: JSON + JPEG выбранного

Ученик   исходящий TCP
         UDP probe
```

Bind `0.0.0.0`. В UDP-ответе — IPv4 того интерфейса, с которого ушёл ответ (или адрес из полученного probe). Несколько NIC: отвечать на probe с того же сокета. WAN-адреса в announce/ручном IP отклоняются.

Школьный Wi‑Fi с client isolation ломает discovery. Основной сценарий — одна LAN без изоляции / проводной кабинет. Запас: ручной ввод IP учителя в UI ученика.

## Стейт-машина ученика

Состояния: `Idle` → `Online` → `Reconnecting` → `Online` | `Ended` → `Idle`.

Единый якорь: `lastRecv` (любой валидный кадр с хаба).

```text
Tick(now):
  if Ended or Idle: return
  silent = now - lastRecv
  if silent >= 10s:  streamPaused = true          // FR-05
  if silent >= 120s: go Ended, fail-open          // FR-06
TcpDrop:
  if Online and silent < 120s: go Reconnecting    // FR-07
session_end:
  go Ended immediately, fail-open                 // FR-08
Reconnect JoinOk:
  lastRecv = now, go Online, streamPaused = false
```

`ShouldHoldPolicies = Online || Reconnecting`  
`ShouldCapture = Online && streamEnabled && !streamPaused`

Teacher.exe / Student.exe **не** завершаются из этих переходов.

Интернет: никакой код не должен вызывать внешний ping и не должен трактовать `NetworkInterface.GetIsNetworkAvailable() == false` как конец класса (на части машин это врёт).

## Потоки

- WPF UI-поток: только отображение (JPEG приходит уже `Freeze()`).
- Приём TCP: отдельный async; маршалинг в UI через Dispatcher.
- Захват DXGI на своём потоке, не UI.
- Вотчер процессов: свой цикл ~500 мс, не UI.

## Логи

`%LocalAppData%\NordControl\logs\teacher-*.log` и `student-*.log`. Ротация: файл до 10 MB, до 3 архивов. Без отправки наружу. Не писать JPEG в лог.

## Безопасность (осознанно слабо)

PIN из 6 смешанных символов — от соседнего кабинета, не от атакующего LAN. Трафик класса без TLS: в школьной LAN это принятый риск MVP. Не делать «шифрования» самописным XOR.

## UI

См. [ui.md](ui.md). PIN только для Join; после Join крестик сворачивает агент в трей.

## Захват и политики

DXGI → JPEG 8–12 fps, длинная сторона ≤1280, только выбранный.

Блоклист exe в RAM, OrdinalIgnoreCase, не трогать `NordControl.*`. Пресет учителя — `%LocalAppData%\NordControl\teacher-preset.json`.
