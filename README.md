# Nord Control

Управление классом в **локальной сети**: учитель видит экран, запускает учебные программы и на время урока блокирует лишнее. Интернет не нужен.

Windows 10/11 x64. Ставить Visual Studio или .NET **не нужно** — это обычные программы, двойной клик.

**Новый пользователь:** [как скачать и проверить на 1 или нескольких ноутах](КАК_ПОПРОБОВАТЬ.md). GitHub предупредит, что это `.exe` — нажми **Скачать в любом случае**.

## Скачать

| Кто | Файл | Ссылка |
|---|---|---|
| **Учитель** (ноут) | `NordControl.Teacher.exe` | [Скачать](https://github.com/Lew1x-swaga/Nord_control_test/releases/latest/download/NordControl.Teacher.exe) |
| **Ученик** (ПК) | `NordControl.Student.exe` | [Скачать](https://github.com/Lew1x-swaga/Nord_control_test/releases/latest/download/NordControl.Student.exe) |

Все версии: [Releases](https://github.com/Lew1x-swaga/Nord_control_test/releases/latest)

> Windows может написать «неизвестный издатель». Это нормально для неподписанного файла: **Подробнее → Выполнить в любом случае**.
> Антивирус иногда ругается на новый exe — добавьте файл в исключения, если уверены, что скачали **отсюда**.

## Как пользоваться (две машины)

1. Ноут и ПК в **одной Wi‑Fi** (или одном кабеле).
2. На ноуте запустите **Учитель** → «Начать класс». Если Windows спросит брандмауэр — разрешите **частные сети**. Запомните PIN (6 символов: буквы и цифры).
3. На чистом ПК скачайте **Ученик**, запустите, введите PIN → «Подключиться».
4. Ученик появится в списке слева. Клик — экран. Дальше запуск и блокировка программ.

Если класс не находится: у ученика «Указать IP учителя вручную» и IPv4 ноута (`ipconfig` на ноуте).

Крестик у ученика во время урока **не выходит из класса** — окно прячется в трей. Выход: трей → «Покинуть урок» или «Выйти из программы».

## Что внутри

- Только LAN, без облака и без интернета как условия работы.
- Блокировки только в памяти агента: закрыли учителя или ученика — ПК снова обычный.
- Два разных exe: роль не угадывается, запускаете нужный файл.

## Для разработчиков

Исходники в этом репозитории. Сборка exe:

```powershell
dotnet publish src/NordControl.Teacher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/teacher
dotnet publish src/NordControl.Student -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/student
```

Спека: [AGENTS.md](AGENTS.md), [требования](docs/requirements.md), [как раздавать](docs/distribution.md).
