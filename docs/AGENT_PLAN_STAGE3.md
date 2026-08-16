# AGENT_PLAN — этап 3 (для ИИ)

```yaml
plan_id: nord-stage-3
stage: 3
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
block_check_interval_ms: 500
protocol_v: 1
preset_filename: "teacher-preset.json"
```

## 0. Кто ты

- **Controller:** читаешь этот файл и `docs/subagents.md`. Диспатчишь **один** `.cursor/agents/nord-stage3-implementer.md` на волну. Не пишешь код сам, если идёт SDD.
- **Implementer:** субагент `nord-stage3-implementer`. Читаешь **только свою волну** + §1–3.
- **Reviewer:** субагент `nord-stage3-reviewer`. После всех волн — `nord-stage3-final-reviewer`.

## 1. READ (строго этот порядок)

1. Этот файл (§0–3 и своя волна)
2. `docs/subagents.md`
3. `docs/requirements.md` — таблица «Этап 3» (FR-30…34)
4. `docs/protocol.md` — сообщения `installed_hints`, `launch_app`, `set_block_list`
5. `docs/invariants.md` — инварианты (политики только в RAM, fail-open, безопасность собственных процессов)
6. `docs/ui.md`

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| Блоклист в RAM агента (`HashSet<string>(StringComparer.OrdinalIgnoreCase)`) | AppLocker, WFP, драйверы, hosts, вечные ключи реестра |
| Проверка только по имени exe-файла (например, `discord.exe`) | Блокировка по заголовку окна как основной ключ |
| Защитный фильтр: **никогда** не трогать свои процессы (`Teacher`, `Student`, префикс `NordControl`, системные среды) | Завершение процесса `NordControl.*` при блоклисте |
| `launch_app`: разовый запуск `Process.Start` без системных хуков | Глобальные хуки внедрения DLL / инжект в процессы |
| `installed_hints`: сбор подсказок об установленных программах для учителя | Диалоговые окна подтверждения у ученика при запуске учителем |
| Fail-open: при `session_end`, таймауте 120 с, выходе агента — немедленный сброс блоклиста в RAM | Сохранение запретов после завершения сессии |
| Пресет учителя: локальный JSON `%LocalAppData%\NordControl\teacher-preset.json` | Облачные базы пресетов, передача файлов пресета наружу |
| `dotnet test` зеленый в конце волны | Коммит без явной просьбы человека |

## 3. Волны

| Wave | Tasks | Название | DONE_WHEN (exit 0) |
|---|---|---|---|
| 1 | 18, 19 | Протокол этапа 3 (DTO `installed_hints`, `launch_app`, `set_block_list`) и тесты | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~Stage3ProtocolTests"` |
| 2 | 20, 21 | Модули `AppBlocker` (RAM-вотчер), `AppLauncher` и `InstalledAppsScanner` | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~AppBlockerTests\|FullyQualifiedName~AppLauncherTests\|FullyQualifiedName~InstalledAppsScannerTests"` |
| 3 | 22, 23 | Интеграция политик и запуска в `ClassHub` и `ClassClient` (Core) + пресет учителя | `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~Stage3CoreTests"` |
| 4 | 24, 25 | UI Учителя (управление пресетами, запуск, блоклист) и сквозные Loopback-тесты | `dotnet test tests/NordControl.Tests` и `dotnet build NordControl.sln` |

## 4. Карта новых и обновляемых файлов

```text
src/NordControl.Protocol/
  WireMessage.cs                    # Расширение DTO: installed_hints, launch_app, set_block_list, InstalledAppInfo

src/NordControl.Core/
  Policies/IAppBlocker.cs           # Интерфейс блокировщика процессов
  Policies/RamAppBlocker.cs         # Реализация RAM-вотчера с защитным фильтром собственных процессов
  Policies/IAppLauncher.cs          # Интерфейс безопасного запуска приложений
  Policies/AppLauncher.cs           # Реализация разового запуска через Process.Start
  Policies/InstalledAppsScanner.cs  # Сбор установленных программ для hints
  Policies/TeacherPreset.cs         # Модель и сериализация teacher-preset.json
  ClassHub.cs                       # Методы SendLaunchAppAsync, SendBlockListAsync, сохранение пресета
  ClassClient.cs                    # Интеграция AppBlocker, AppLauncher, отправка installed_hints, fail-open

src/NordControl.Teacher/
  MainWindow.xaml                   # Вкладка/панель управления: пресеты, запуск ПО, управление блоклистом
  MainWindow.xaml.cs                # Обработчики применения пресетов, отправка команд выбранному/всем

tests/NordControl.Tests/
  Stage3ProtocolTests.cs            # Тесты сериализации/десериализации сообщений этапа 3
  AppBlockerTests.cs                # Тесты RAM-блокировщика, защита собственных процессов, fail-open сброс
  AppLauncherTests.cs               # Тесты запуска программ и валидации путей
  InstalledAppsScannerTests.cs      # Тесты сбора установленных приложений
  Stage3CoreTests.cs                # Тесты отправки и применения политик между Hub и Client
  LoopbackStage3Tests.cs            # Сквозной интеграционный тест Stage 3 (запуск, блоклист, fail-open)
```

---

## Wave 1 / Task 18, 19: Протокол этапа 3 (DTO и сериализация)

- **DEPENDS_ON:** Stage 2
- **WAVE:** 1
- **SKILL:** test-driven-development
- **DONE_WHEN:** `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~Stage3ProtocolTests"` PASS
- **Files:** `src/NordControl.Protocol/WireMessage.cs`, `tests/NordControl.Tests/Stage3ProtocolTests.cs`

**Спецификация сообщений JSON:**
- `installed_hints`:
  ```json
  {
    "v": 1,
    "type": "installed_hints",
    "apps": [
      { "name": "Google Chrome", "exe": "chrome.exe", "launch_target": "C:\\Program Files\\...\\chrome.exe" }
    ]
  }
  ```
- `launch_app`:
  ```json
  {
    "v": 1,
    "type": "launch_app",
    "exe": "chrome.exe",
    "launch_target": "C:\\Program Files\\...\\chrome.exe"
  }
  ```
- `set_block_list`:
  ```json
  {
    "v": 1,
    "type": "set_block_list",
    "exe_names": ["discord.exe", "steam.exe"]
  }
  ```

---

## Wave 2 / Task 20, 21: RAM-блокировщик, лаунчер и сканер ПО (Core / Student)

- **DEPENDS_ON:** Wave 1
- **WAVE:** 2
- **SKILL:** test-driven-development
- **DONE_WHEN:** `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~AppBlockerTests|FullyQualifiedName~AppLauncherTests|FullyQualifiedName~InstalledAppsScannerTests"` PASS
- **Files:** `src/NordControl.Core/Policies/*`, тесты

**Поведение `RamAppBlocker`:**
- `SetBlockList(IEnumerable<string> exeNames)`: сохраняет список в памяти (`OrdinalIgnoreCase`, только имя файла).
- `Clear()`: очищает список в памяти.
- Фоновый цикл (период ~500 мс): проверяет запущенные процессы.
- **Защитный фильтр:** процессы с именами `Teacher`, `Student`, префиксом `NordControl`, `dotnet`, `devenv`, системные `explorer`, `dwm` **категорически запрещено завершать**.
- Если процесс из блоклиста обнаружен $\rightarrow$ попытка закрытия `process.Kill()`.
- При вызове `Clear()` или уничтожении объекта — моментальный сброс (fail-open).

**Поведение `AppLauncher`:**
- `bool Launch(string exe, string? launchTarget)`: запуск через `Process.Start(new ProcessStartInfo { FileName = ..., UseShellExecute = true })` с безопасной обработкой исключений.

**Поведение `InstalledAppsScanner`:**
- Сбор популярных ярлыков из Start Menu и реестра `Uninstall` (браузеры, офисные пакеты, IDE).

---

## Wave 3 / Task 22, 23: Интеграция в ClassHub, ClassClient и пресет учителя

- **DEPENDS_ON:** Wave 2
- **WAVE:** 3
- **SKILL:** test-driven-development
- **DONE_WHEN:** `dotnet test tests/NordControl.Tests --filter "FullyQualifiedName~Stage3CoreTests"` PASS
- **Files:** `src/NordControl.Core/ClassHub.cs`, `src/NordControl.Core/ClassClient.cs`, `src/NordControl.Core/Policies/TeacherPreset.cs`, тесты

**Поведение `ClassHub`:**
- `SendLaunchAppAsync(string studentId, string exe, string? launchTarget)`: отправка `launch_app` конкретному ученику.
- `BroadcastLaunchAppAsync(string exe, string? launchTarget)`: отправка `launch_app` всем онлайн-ученикам.
- `SendBlockListAsync(string studentId, IReadOnlyList<string> exeNames)`: отправка `set_block_list` конкретному ученику.
- `BroadcastBlockListAsync(IReadOnlyList<string> exeNames)`: отправка `set_block_list` всем онлайн-ученикам.
- `TeacherPresetManager`: загрузка и сохранение `%LocalAppData%\NordControl\teacher-preset.json`.
- Событие `InstalledHintsReceived(string studentId, IReadOnlyList<InstalledAppInfo> apps)`.

**Поведение `ClassClient`:**
- При получении `launch_app` $\rightarrow$ вызов `AppLauncher.Launch`.
- При получении `set_block_list` $\rightarrow$ вызов `AppBlocker.SetBlockList`.
- При `session_end`, переходе в `Ended` или сбросе в `Idle` $\rightarrow$ вызов `AppBlocker.Clear()` (fail-open).
- При входе в `Online` $\rightarrow$ автоматическая отправка `installed_hints` учителю.

---

## Wave 4 / Task 24, 25: UI Учителя и сквозное тестирование

- **DEPENDS_ON:** Wave 3
- **WAVE:** 4
- **SKILL:** verification-before-completion
- **DONE_WHEN:** `dotnet test tests/NordControl.Tests` PASS и `dotnet build NordControl.sln` PASS
- **Files:** `src/NordControl.Teacher/MainWindow.xaml`, `MainWindow.xaml.cs`, `tests/NordControl.Tests/LoopbackStage3Tests.cs`

**Поведение UI Учителя:**
- Панель управления программами:
  - Список разрешенных / быстрых для запуска программ с кнопками «Запустить у выбранного» и «Запустить у всех».
  - Список блокируемых программ (чекбоксы / управление именами exe) с кнопкой «Применить блоклист».
  - Быстрое добавление в блоклист по правому клику / кнопке из таблицы активных процессов ученика.
  - Сохранение пресета учителя при закрытии или изменении.
- **Интеграционный тест `LoopbackStage3Tests`:**
  - Проверка отправки и получения `installed_hints`.
  - Проверка доставки команды `launch_app`.
  - Проверка доставки `set_block_list`, работы блокировки в памяти и немедленного снятия (fail-open) при `StopClassAsync()`.

---

## Инварианты Этапа 3

1. **Политики только в RAM:** Никаких постоянных системных блокировок (AppLocker, драйверы, hosts, WFP).
2. **Fail-Open:** Любое завершение сессии (`session_end`, таймаут 120 с, закрытие процесса) немедленно и полностью отключает блокировку.
3. **Безопасность собственных процессов:** Блокировщик **никогда** не закрывает `NordControl.*`, `Teacher`, `Student` или критические системные процессы.
4. **Регистронезависимое сравнение:** Имена exe проверяются через `StringComparer.OrdinalIgnoreCase`.
5. **Разовый запуск:** `launch_app` — это простой вызов `Process.Start`, без перехвата управления ОС.
