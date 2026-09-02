---
name: executing-nord-stage8
description: Use when implementing Nord Control stage 8 (one-way teacher_message notice with close button). Executes docs/AGENT_PLAN_STAGE8.md. Not a chat. Do not use for screen preview or installer.
---

# Executing Nord Control stage 8

1. Stage 7 should be done. Read `docs/AGENT_PLAN_STAGE8.md` then `docs/subagents.md`.
2. One implementer: `.cursor/agents/nord-stage8-implementer.md`.
3. Waves **1→4**. `DONE_WHEN` exit 0.
4. After wave: `nord-stage8-reviewer`. Fixer = same implementer.
5. After wave 4: `nord-stage8-final-reviewer`. Do not start stage 9 unless asked.

Forbidden: student reply, chat log, auto-dismiss timer like ToastWindow, screen lock, JPEG type 3, MSI, commit unless asked. Do not log message text.
