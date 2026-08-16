# Субагенты и навыки (этап 1)

Файлы агентов Cursor (системный промпт):

| Файл | Имя | Когда звать |
|---|---|---|
| `.cursor/agents/nord-stage1-implementer.md` | `nord-stage1-implementer` | одна волна кода |
| `.cursor/agents/nord-stage1-reviewer.md` | `nord-stage1-reviewer` | сразу после волны |
| `.cursor/agents/nord-stage1-final-reviewer.md` | `nord-stage1-final-reviewer` | после волны 5, `dotnet test` зелёный |

Для модели: задачи в `AGENT_PLAN.md` **последовательные** — их нельзя делать параллельно в одном дереве файлов.

## Число

| Параметр | Значение | Почему |
|---|---|---|
| `concurrent_implementers` | **1** | Один sln, общие типы. Параллельные писатели конфликтуют |
| `waves` | **5** | Склеены зависимые Task 1–9 |
| `reviewer_per_wave` | **1** | После каждой волны, не после каждого мелкого шага |
| `final_reviewer` | **1** | Когда `dotnet test` зелёный |
| Всего вызовов | **11** (5+5+1) | Не 9 параллельных implementer |

Не путать «5 волн» с «5 одновременных агентов». Живой writer всегда один.

## Волны

| Wave | Tasks | Роль | REQUIRED skill | Модель |
|---|---|---|---|---|
| 1 | 1–3 | solution + Protocol + `StudentSession` | `test-driven-development` | standard |
| 2 | 4 | `ClassHub` | `test-driven-development` | standard |
| 3 | 5 | `ClassClient` | `test-driven-development` | standard |
| 4 | 6–7 | WPF Teacher + Student | нет TDD на XAML; не ломать тесты Core | standard |
| 5 | 8–9 | loopback + `dotnet test` | `test-driven-development` (loopback), `verification-before-completion` | standard |

Если тесты красные: `systematic-debugging`, не «добавить JPEG».

После каждой волны: субагент `nord-stage1-reviewer`. Смотрит: FR этапа 1, инварианты, нет этапа 2.

В конце: `nord-stage1-final-reviewer` по всему этапу 1.

## Что не вызывать

- `brainstorming`, `writing-plans` — концепт закрыт
- `using-git-worktrees` — только если человек попросил отдельное дерево
- параллельный `dispatching-parallel-agents` на запись в этот репозиторий

## Бриф субагенту (шаблон)

```text
ROLE: implementer wave N
READ: docs/AGENT_PLAN.md (only Wave N + YAML header + Global Constraints)
READ: docs/protocol.md (if wave 1-3,5)
DONE_WHEN: <command from plan>
DO_NOT: stage 2, commit unless user asked, internet ping
SKILL: <from table>
```

Контроллер не копирует историю чата в бриф.
