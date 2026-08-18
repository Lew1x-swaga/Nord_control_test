# Как приложение попадает к пользователю

Лицензий в MVP нет. Онлайн-активации нет (кабинет должен жить без интернета).

## Сейчас — GitHub Releases (два exe)

Обычный путь для школы и для теста на чистом ПК:

1. Страница репозитория → блок **Скачать** в README, либо [Releases](https://github.com/Lew1x-swaga/Nord_control_test/releases/latest).
2. Учитель качает `NordControl.Teacher.exe`, ученик — `NordControl.Student.exe`.
3. Двойной клик. .NET SDK на машине не нужен (self-contained, один файл).
4. Первый запуск Teacher: Windows спросит брандмауэр — разрешить **частные** сети.

Сборка на машине разработки (то же, что кладёт CI в релиз):

```powershell
dotnet publish src/NordControl.Teacher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/teacher
dotnet publish src/NordControl.Student -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/student
```

Тег `v*` на GitHub запускает `.github/workflows/release.yml` и прикрепляет оба exe к релизу.

Роль не угадывается: запускается конкретный exe.

## Потом — один Setup (этап 4, не сейчас)

Inno Setup или MSI: роль Учитель/Ученик; у учителя правило брандмауэра; у ученика **без** автозапуска в MVP; uninstall чистит файлы (политик в ОС мы не писали).

Школе тот же файл + офлайн-папка на флешке. Store не используем.

Обновления: новая папка / новый Setup, не интернет-апдейтер в ядре.

## Удаление и reboot

| Действие | Ожидание |
|---|---|
| Удалить / стереть папку | Агента нет, запретов нет |
| Закрыть Teacher | `session_end`, ученик без запретов |
| Reboot ученика (MVP) | Агент не стартует |
| Kill агента | Политики в RAM умерли |

Служба автозапуска — этап 5: после reboot ждёт учителя, блоклиста нет.
