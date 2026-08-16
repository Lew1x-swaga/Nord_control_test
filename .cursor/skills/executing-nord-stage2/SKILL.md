---
name: executing-nord-stage2
description: Use when implementing Nord Control stage 2, executing docs/AGENT_PLAN_STAGE2.md, dispatching DXGI screen capture / process list / streaming subagents. Do not use for stage 3 app blocking or process killing.
---

# Executing Nord Control stage 2

## Instructions

1. Read `docs/AGENT_PLAN_STAGE2.md` section 0 (YAML) then `docs/subagents.md`.
2. Concurrent implementers = **1**. Dispatch `.cursor/agents/nord-stage2-implementer.md` only.
3. Execute **waves 1→4** in order. A wave is done only when its `DONE_WHEN` command exits 0.
4. After each wave: `.cursor/agents/nord-stage2-reviewer.md`. Then next wave.
5. After wave 4: `.cursor/agents/nord-stage2-final-reviewer.md`. Stop. Do not start stage 3.

## Subagent prompt (implementer)

Give the subagent only: wave id, task ids, `DONE_WHEN`, file list, Global Constraints from the plan. Do not paste the whole conversation.

## Forbidden

App blocking, process killing (`set_block_list`, `launch_app`), autostart, commit unless user asked, `Environment.Exit` on socket errors, internet ping, JPEG in JSON/base64 (must be binary Type 2 frame), streaming multiple students simultaneously.
