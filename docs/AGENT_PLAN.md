# AGENT_PLAN — этап 1

## Реализовано

- Решение `NordControl.sln`: Protocol, Core, Teacher, Student, xUnit-тесты.
- UDP discovery (`NORD1` probe/announce без PIN) и TCP-кадры (`FrameCodec`, JSON type 1).
- Сессия ученика: `Idle` / `Online` / `Reconnecting` / `Ended`; якорь `lastRecv`; stale 10 с; fail-open 120 с; явный `session_end` сразу.
- `ClassHub` / `ClassClient`: Join по PIN, heartbeat, reconnect по `session_token`, ручной IP.
- WPF: список класса у учителя, плашка + трей у ученика; loopback-тесты на портах 47830/47831.

## Архитектурная справка

Константы — только `ProtocolConstants` (порты 47820/47821, heartbeat 3 с, stale 10 с, reconnect 120 с, magic `NORD1`). PIN на проводе — строка из 6 символов (3 буквы A–Z + 3 цифры), не в UDP.

Ключевые файлы: `FrameCodec`, `UdpPackets`, `WireMessage`, `StudentSession`, `ClassHub`, `ClassClient`, WPF Teacher/Student.

Инварианты: нет `Environment.Exit` из сокета, нет ping/HTTP как health-check, Protocol без Win32. Логи: `%LocalAppData%\NordControl\logs\`, PIN не пишется.
