# AGENTS

Этапы 1–3 и ручной QA закрыты (продукт v0.1.2). Документы ниже — эталон спецификации, не очередь волн. Дальше — этап 4+ по `docs/roadmap.md`.

| Order | File | Purpose |
|---|---|---|
| 1 | `docs/roadmap.md` | Статусы этапов; 4 и 5 ещё впереди |
| 2 | `docs/requirements.md` | FR-01…13, FR-20…23, FR-30…34 |
| 3 | `docs/protocol.md` | Бинарные TCP кадры (JSON Type 1, JPEG Type 2) |
| 4 | `docs/invariants.md` | LAN-only, fail-open, RAM-only |
| 5 | `docs/ui.md` | Спецификация UI Учителя и Ученика |
| 6 | `docs/architecture.md` | Компоненты, стейт-машина, потоки |
| 7 | `docs/AGENT_PLAN.md` | Справка этапа 1 (LAN, PIN, heartbeat) |
| 8 | `docs/AGENT_PLAN_STAGE2.md` | Справка этапа 2 (JPEG, список окон) |
| 9 | `docs/AGENT_PLAN_STAGE3.md` | Справка этапа 3 (launch, RAM-блоклист, пресет) |

Rules: `.cursor/rules/` (always: `00-agent-entry`, `invariants`).

Не диспатчить `nord-stage1/2/3-implementer` для повторной реализации закрытых этапов. Commit только если человек попросил.
