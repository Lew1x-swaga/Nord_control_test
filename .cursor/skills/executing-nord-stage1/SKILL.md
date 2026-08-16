---
name: executing-nord-stage1
description: Use when implementing Nord Control stage 1, executing docs/AGENT_PLAN.md, dispatching Teacher/Student LAN subagents, or when the user asks to write connection/PIN/heartbeat code. Do not use for screen capture, JPEG, or app blocking.
---

# Executing Nord Control stage 1

## Instructions

1. Read `docs/AGENT_PLAN.md` section 0 (YAML) then `docs/subagents.md`.
2. Concurrent implementers = **1**. Dispatch `.cursor/agents/nord-stage1-implementer.md` only.
3. Execute **waves 1→5** in order. A wave is done only when its `DONE_WHEN` command exits 0.
4. After each wave: `.cursor/agents/nord-stage1-reviewer.md`. Then next wave.
5. After wave 5: `.cursor/agents/nord-stage1-final-reviewer.md`. Stop. Do not start stage 2.

## Subagent prompt (implementer)

Give the subagent only: wave id, task ids, `DONE_WHEN`, file list, Global Constraints from the plan. Do not paste the whole conversation.

Required skills for implementer: see wave table in `docs/subagents.md`.

## Forbidden

DXGI, JPEG, blocklist, autostart, commit unless the user asked, `Environment.Exit` on socket errors, internet ping.
