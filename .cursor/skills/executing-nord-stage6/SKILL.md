---
name: executing-nord-stage6
description: Use when implementing Nord Control stage 6 (teacher student-list grid vs list). Executes docs/AGENT_PLAN_STAGE6.md. Do not use for screen preview grid, groups, chat, installer.
---

# Executing Nord Control stage 6

1. Read `docs/AGENT_PLAN_STAGE6.md` YAML then `docs/subagents.md`.
2. Concurrent implementers = **1**. Dispatch `.cursor/agents/nord-stage6-implementer.md` only.
3. Waves **1→3**. Wave done only when `DONE_WHEN` exits 0.
4. After each wave: `.cursor/agents/nord-stage6-reviewer.md`. FAIL → same implementer as fixer.
5. After wave 3: `.cursor/agents/nord-stage6-final-reviewer.md`. Stop. Do not start stage 7 unless the human says so.

Prompt implementer: wave id, task ids, `DONE_WHEN`, file list, DO/DON'T. Do not paste the chat.

Forbidden: join cap 24, JPEG on student cards, type 3 preview, groups, teacher_message, MSI, commit unless asked.
