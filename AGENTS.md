# AGENTS

This repo is implemented by AI against a frozen spec. Do not invent stage 2.

| Order | File |
|---|---|
| 1 | `docs/AGENT_PLAN.md` |
| 2 | `docs/subagents.md` |
| 3 | `docs/requirements.md` (only FR-01…13 until stage 1 accepted) |
| 4 | `docs/protocol.md` |
| 5 | `docs/invariants.md` |
| 6 | `docs/ui.md` (waves 4+) |

Rules: `.cursor/rules/` (always: `00-agent-entry`, `invariants`).

Skill: `.cursor/skills/executing-nord-stage1/SKILL.md`

Субагенты: `.cursor/agents/nord-stage1-implementer.md`, `nord-stage1-reviewer.md`, `nord-stage1-final-reviewer.md`.

**n = 1** implementer at a time. **5 waves.** Superpowers: `test-driven-development` (waves 1–3, 5), `systematic-debugging` on failure, `verification-before-completion` at the end. Do not run `brainstorming` or `writing-plans` unless the human asks to change the spec.

Commit only if the human asked.
