# Архитектура

Статус: Teacher Hub принят; heartbeat — переживает обрыв LAN и не зависит от интернета.

Стек зафиксирован: **C# / .NET 8 / WPF**. Не WinUI, не Electron.

## Компоненты

```text
NordControl.sln
  src/NordControl.Protocol    кадры TCP, JSON, UDP-строки, константы
  src/NordControl.Core        стейт-машина, ClassHub, ClassClient; позже Policy
  src/NordControl.Teacher     только WPF поверх ClassHub
  src/NordControl.Student     только WPF/tray поверх ClassClient
  tests/NordControl.Tests     xUnit (net8.0), без WPF
```

Хабы в Core, не в WPF: иначе loopback-тесты не смогут сослаться на `net8.0-windows`.

| Модуль | Ответственность |
|---|---|
| Protocol | На проводе. Без WPF и Win32 |
| Core | `StudentSession`, `ClassHub`, `ClassClient`. На этапе 3 — `ProcessPolicy` |
| Teacher | UI списка, PIN, старт/стоп класса |
| Student | Плашка, трей, поле PIN/IP |

Этап 2: захват DXGI живёт в Student (Windows), в Protocol уходит JPEG как `messageType=2`. Не делать, пока этап 1 не принят. Оркестрация кода: `docs/subagents.md`.  
Этап 3: Policy только в Core/Student, в RAM.  
Позже: Windows Service для автозапуска — тот же Core.

## Топология

```text
Учитель  UDP 47820  announce (без PIN)
         TCP 47821  сессия: JSON + позже JPEG

Ученик   исходящий TCP
         UDP probe
```

Bind `0.0.0.0`. В UDP-ответе — IPv4 того интерфейса, с которого ушёл ответ (или адрес из полученного probe). Несколько NIC: отвечать на probe с того же сокета.

Школьный Wi‑Fi с client isolation ломает discovery. Основной сценарий — одна LAN без изоляции / проводной кабинет. Ручной ввод IP учителя: поле в UI ученика можно сделать уже на этапе 1 как запас (маленькая форма «IP, если класс не найден») — полезно для двух машин с изоляцией.

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

- WPF UI-поток: только отображение.
- Приём TCP: отдельный async; маршалинг в UI через Dispatcher.
- Этап 2: захват DXGI на своём потоке, не UI.
- Вотчер процессов (этап 3): свой цикл ~500 мс, не UI.

## Логи

`%LocalAppData%\NordControl\logs\teacher-*.log` и `student-*.log`. Вращение простое (новый файл за день). Без отправки наружу. Не писать JPEG в лог.

## Безопасность (осознанно слабо)

PIN из 6 смешанных символов — от соседнего кабинета, не от атакующего LAN. Трафик класса без TLS: в школьной LAN это принятый риск MVP. Не делать «шифрования» самописным XOR.

## UI

См. [ui.md](ui.md). PIN только для Join; после Join крестик сворачивает агент в трей.

## Захват и политики (этапы 2–3, не реализовывать в первой сессии кода)

DXGI → JPEG 8–12 fps, длинная сторона ≤1280, только выбранный.

Блоклист exe в RAM, OrdinalIgnoreCase, не трогать `NordControl.*`. Пресет учителя — `%LocalAppData%\NordControl\teacher-preset.json`.
