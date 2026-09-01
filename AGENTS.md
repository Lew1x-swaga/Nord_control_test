# AGENTS

Этапы 1–3 закрыты (v1.0.5). Дальше — **только** по `docs/roadmap.md` и `docs/AGENT_PLAN_STAGE*.md`. Оркестрация: `docs/subagents.md`.

| Order | File | Purpose |
|---|---|---|
| 1 | `docs/roadmap.md` | Статусы; урок 6–9; поставка 4–5 |
| 2 | `docs/requirements.md` | FR-01…34 готово; FR-60…94 и FR-40…54 — спека |
| 3 | `docs/protocol.md` | TCP кадры; `teacher_message`; JPEG type 3 |
| 4 | `docs/invariants.md` | LAN, fail-open, RAM, один HD |
| 5 | `docs/ui.md` | Учитель / ученик |
| 6 | `docs/architecture.md` | Компоненты, стейт-машина |
| 7 | `docs/AGENT_PLAN.md` | Справка этапа 1 (не волны) |
| 8 | `docs/AGENT_PLAN_STAGE2.md` | Справка этапа 2 |
| 9 | `docs/AGENT_PLAN_STAGE3.md` | Справка этапа 3 |
| 10 | `docs/AGENT_PLAN_STAGE6.md` | **Волны этапа 6** (делать первым из урока) |
| 11 | `docs/AGENT_PLAN_STAGE7.md` | Волны этапа 7 |
| 12 | `docs/AGENT_PLAN_STAGE8.md` | Волны этапа 8 |
| 13 | `docs/AGENT_PLAN_STAGE9.md` | Волны этапа 9 |
| 14 | `docs/AGENT_PLAN_STAGE4.md` | Установщик — только по просьбе |
| 15 | `docs/AGENT_PLAN_STAGE5.md` | Школа — только по просьбе |
| 16 | `docs/subagents.md` | Кто кого диспатчит |

Rules: `.cursor/rules/` (`00-agent-entry`, `stage-plans`, `invariants`).

Не диспатчить `nord-stage1/2/3-implementer`. Один implementer. Commit только если человек попросил.
