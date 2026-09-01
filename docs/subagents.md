# Субагенты и навыки

Этапы 1–3 закрыты: `.cursor/agents/nord-stage1-*` … `nord-stage3-*` **не вызывать**.

| Параметр | Значение | Почему |
|---|---|---|
| `concurrent_implementers` | **1** | Один sln. Два писателя = конфликт |
| Порядок урока | **6 → 7 → 8 → 9** | Как `docs/roadmap.md` |
| Поставка | **4, затем 5** | Только если человек просил Setup/службу |

Контроллер читает план этапа и skill `executing-nord-stageN`. Код пишет только implementer одной волны.

## Контур урока

| Этап | План | Skill | Implementer | Reviewer | Final |
|---|---|---|---|---|---|
| 6 список-сетка | `docs/AGENT_PLAN_STAGE6.md` | `executing-nord-stage6` | `nord-stage6-implementer` | `nord-stage6-reviewer` | `nord-stage6-final-reviewer` |
| 7 группы | `docs/AGENT_PLAN_STAGE7.md` | `executing-nord-stage7` | `nord-stage7-implementer` | `nord-stage7-reviewer` | `nord-stage7-final-reviewer` |
| 8 сообщение | `docs/AGENT_PLAN_STAGE8.md` | `executing-nord-stage8` | `nord-stage8-implementer` | `nord-stage8-reviewer` | `nord-stage8-final-reviewer` |
| 9 экраны-сетка | `docs/AGENT_PLAN_STAGE9.md` | `executing-nord-stage9` | `nord-stage9-implementer` | `nord-stage9-reviewer` | `nord-stage9-final-reviewer` |

Цикл волны: implementer → `DONE_WHEN` exit 0 → reviewer. Spec FAIL или Quality = Changes required → **тот же** implementer (fixer), не новый параллельный. После последней волны → final-reviewer. READY → следующий этап **только** если человек или роадмап велят.

Промпт implementer: wave id, task ids, `DONE_WHEN`, список файлов, DO/DON'T плана. Не вставлять весь чат.

## Поставка (гейт)

| Этап | План | Skill | Implementer | Reviewer | Final |
|---|---|---|---|---|---|
| 4 установщик | `docs/AGENT_PLAN_STAGE4.md` | `executing-nord-stage4` | `nord-stage4-implementer` | `nord-stage4-reviewer` | `nord-stage4-final-reviewer` |
| 5 школа | `docs/AGENT_PLAN_STAGE5.md` | `executing-nord-stage5` | `nord-stage5-implementer` | `nord-stage5-reviewer` | `nord-stage5-final-reviewer` |

Нет явной просьбы про Setup/службу → не диспатчить 4 и 5.

## Что не вызывать

- Повтор волн закрытых этапов 1–3
- `dispatching-parallel-agents` на запись в этот репозиторий
- Этап N+1, пока final N не READY (урок) или человек не сказал иначе
- Сетку экранов (9) в одном PR с сеткой списка (6)
