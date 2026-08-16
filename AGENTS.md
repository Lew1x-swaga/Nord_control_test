# AGENTS

This repo is implemented by AI against frozen specs per stage.

| Order | File | Purpose |
|---|---|---|
| 1 | `docs/AGENT_PLAN.md` | Этап 1: LAN связь, сессии, PIN, heartbeat (Готово) |
| 2 | `docs/AGENT_PLAN_STAGE2.md` | Этап 2: Захват экрана JPEG (DXGI/GDI) + список процессов |
| 3 | `docs/AGENT_PLAN_STAGE3.md` | Этап 3: Запуск приложений и RAM-блокировка (`launch_app`, `set_block_list`, пресет) |
| 4 | `docs/subagents.md` | Оркестрация субагентов для этапов 1, 2 и 3 |
| 5 | `docs/requirements.md` | Требования FR-01…13 (этап 1), FR-20…23 (этап 2), FR-30…34 (этап 3) |
| 6 | `docs/protocol.md` | Бинарные TCP кадры (JSON Type 1, JPEG Type 2) |
| 7 | `docs/invariants.md` | Инварианты безопасности, LAN-only, fail-open, RAM-only |
| 8 | `docs/ui.md` | Спецификация UI Учителя и Ученика |

Rules: `.cursor/rules/` (always: `00-agent-entry`, `invariants`).

Субагенты:
- Этап 1: `.cursor/agents/nord-stage1-implementer.md`, `nord-stage1-reviewer.md`, `nord-stage1-final-reviewer.md`
- Этап 2: `.cursor/agents/nord-stage2-implementer.md`, `nord-stage2-reviewer.md`, `nord-stage2-final-reviewer.md`
- Этап 3: `.cursor/agents/nord-stage3-implementer.md`, `nord-stage3-reviewer.md`, `nord-stage3-final-reviewer.md`

Skills:
- `.cursor/skills/executing-nord-stage1/SKILL.md`
- `.cursor/skills/executing-nord-stage2/SKILL.md`
- `.cursor/skills/executing-nord-stage3/SKILL.md`

**n = 1** implementer at a time.
Stage 1: 5 waves. Stage 2: 4 waves. Stage 3: 4 waves.
Superpowers: `test-driven-development`, `systematic-debugging` on failure, `verification-before-completion` at the end.

Commit only if the human asked.
