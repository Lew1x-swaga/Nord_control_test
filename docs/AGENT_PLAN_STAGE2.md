# AGENT_PLAN — этап 2

## Реализовано

- Бинарный JPEG-кадр TCP type 2: `width`, `height`, `ts_ms`, jpeg-байты (не JSON/base64).
- Захват экрана ученика: DXGI Desktop Duplication с fallback на GDI; длинная сторона ≤1280, 8–12 fps, качество ~70.
- Один видеопоток: `stream_start` / `stream_stop` только выбранному; пауза при `StreamPaused` (stale ≥10 с) или не `Online`.
- Список оконных процессов (~40, раз в 2.5 с) + активный exe; UI учителя — JPEG и таблица процессов.
- Loopback-тесты стрима и `process_list`.

## Архитектурная справка

Захват живёт в Student (`DxgiScreenCapturer` / `GdiScreenCapturer`), не в Protocol. Core маршрутизирует кадры (`SelectStudent`, события кадра и `process_list`). Protocol без `System.Drawing` / WPF / Win32.

Инварианты: максимум один JPEG в сети; LAN-only; надзор видимый (плашка/баннер); экран на диск не пишется.
