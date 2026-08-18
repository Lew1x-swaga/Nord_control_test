# AGENT_PLAN — этап 3

## Реализовано

- Протокол: `installed_hints`, `launch_app`, `set_block_list` (имена exe, OrdinalIgnoreCase).
- `RamAppBlocker` в RAM агента (~500 мс): нашёл запрещённый exe — закрыл; пустой список — без опроса процессов.
- `AppLauncher` — разовый `Process.Start`; `InstalledAppsScanner` — подсказки учителю.
- Пресет учителя: `%LocalAppData%\NordControl\teacher-preset.json`. UI: запуск выбранному/всем, блоклист, hints.
- Fail-open: `session_end` / `Ended` / смерть процесса → `AppBlocker.Clear()` сразу. Свои процессы (`NordControl.*`, Teacher, Student) не трогать.

## Архитектурная справка

Политики только в памяти агента. Нет AppLocker, WFP, hosts, драйверов, вечных ключей реестра. Сравнение exe — имя файла, не заголовок окна.

Ключевые типы: `IAppBlocker` / `RamAppBlocker`, `IAppLauncher` / `AppLauncher`, `InstalledAppsScanner`, `TeacherPreset`. Этап 4 (установщик, служба) — вне этого плана.
