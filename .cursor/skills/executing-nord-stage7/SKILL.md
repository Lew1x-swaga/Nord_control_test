---
name: executing-nord-stage7
description: Use when implementing Nord Control stage 7 (RAM student groups, launch/block to a group). Executes docs/AGENT_PLAN_STAGE7.md. Do not use for chat, screen previews, installer.
---

# Executing Nord Control stage 7

1. Stage 6 should be done. Read `docs/AGENT_PLAN_STAGE7.md` then `docs/subagents.md`.
2. One implementer: `.cursor/agents/nord-stage7-implementer.md`.
3. Waves **1→4**. `DONE_WHEN` exit 0 required.
4. After wave: `nord-stage7-reviewer`. Fixer = same implementer.
5. After wave 4: `nord-stage7-final-reviewer`. Do not start stage 8 unless asked.

Forbidden: new JSON type for membership, group PIN, file drop, teacher_message, preview type 3, MSI, commit unless asked.
