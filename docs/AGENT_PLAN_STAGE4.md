# AGENT_PLAN — этап 4 (для ИИ)

Установщик. **Не начинать**, пока человек явно не попросил Setup/MSI/«поставка». Не блокирует этапы 6–9 и не делается в одном PR с ними.

```yaml
plan_id: nord-stage-4
stage: 4
depends_on: []
requires_user_request: true
blocks_classroom: false
commit: only_if_user_asked
concurrent_implementers: 1
waves: 3
reviewer_per_wave: 1
final_reviewer: 1
os: windows-x64
dotnet: "8.0"
```

## 0. Субагенты

- **Controller:** `executing-nord-stage4`. Если человек не просил установщик — **стоп**, не диспатчить.
- **Implementer:** `.cursor/agents/nord-stage4-implementer.md`
- **Reviewer:** `.cursor/agents/nord-stage4-reviewer.md`
- **Fixer:** тот же implementer
- **Final:** `.cursor/agents/nord-stage4-final-reviewer.md`. Не этап 5, пока не попросили.

Не мешать с группами/сеткой/сообщениями. Не служба автозапуска (это этап 5).

Сборка Inno/MSI — **Windows**. На Linux-агенте: только скрипты и docs; `DONE_WHEN` волны 2 = файлы на месте, не молчаливый skip без записи в отчёт.

## 1. READ

1. Этот файл + волна
2. `docs/subagents.md`
3. `docs/requirements.md` — FR-40…44
4. `docs/distribution.md`
5. `.cursor/rules/github-exes.mdc` — self-contained publish **не заменяет** Setup, но путь exe жив (FR-44)
6. `.github/workflows/release.yml`

## 2. DO / DON'T

| DO | DON'T |
|---|---|
| Inno Setup **или** MSI: роль Учитель/Ученик | Store, онлайн-активация |
| Firewall inbound Teacher 47820/47821 private | Автозапуск ученика (этап 5) |
| Офлайн-папка + инструкция | Политики AppLocker/WFP |
| Uninstall чистит файлы программы | Интернет-апдейтер в ядре |
| Сохранить `dotnet publish` single-file | Ломать GitHub Releases exe |

## 3. Волны

| Wave | Tasks | Название | DONE_WHEN |
|---|---|---|---|
| 1 | 41 | Спека Setup в docs + скрипт-заготовка | файлы из карты волны 1 существуют; `dotnet test tests/NordControl.Tests` зелёный (регрессия) |
| 2 | 42 | Скрипт установщика (iss/wix) + firewall | на Windows: installer собирается; иначе iss/wix валиден как текст + отчёт |
| 3 | 43 | README/distribution офлайн-пакет | `docs/distribution.md` обновлён; test+sln зелёные |

## 4. Карта файлов

```text
docs/distribution.md          # раздел Setup, ключи, флешка
installer/NordControl.iss     # или wix/*.wxs — один стек, не оба
installer/README.md           # как собрать на машине с Inno
.github/workflows/            # опционально артефакт installer; не ломать exe-release
```

Код Protocol/Core/WPF не менять без нужды. Нет новых FR урока.

---

## Wave 1 / Task 41

Зафиксировать: компоненты Teacher vs Student; правило брандмауэра только Teacher; ученик без inbound; upgrade = новый Setup.

Не писать службу.

## Wave 2 / Task 42

Firewall: `netsh advfirewall` или API установщика, профиль Private. Не Domain-only если кабинет в workgroup.

## Wave 3 / Task 43

Офлайн: положить оба exe + Setup на флешку. Uninstall: нет хвостов политик.

## Приёмка человеком

Чистая Windows: Setup → Teacher+Student в LAN Join. Uninstall. Путь «скачал два exe» всё ещё работает.
