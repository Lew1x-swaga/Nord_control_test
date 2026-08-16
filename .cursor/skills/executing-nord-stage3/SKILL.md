---
name: executing-nord-stage3
description: Use when implementing Nord Control stage 3, executing docs/AGENT_PLAN_STAGE3.md, dispatching RAM AppBlocker / AppLauncher / Teacher preset subagents. Do not use for stage 4 installer, Windows service or autostart.
---

# Executing Nord Control stage 3

## Instructions

1. Read `docs/AGENT_PLAN_STAGE3.md` section 0 (YAML) then `docs/subagents.md`.
2. Concurrent implementers = **1**. Dispatch `.cursor/agents/nord-stage3-implementer.md` only.
3. Execute **waves 1→4** in order. A wave is done only when its `DONE_WHEN` command exits 0.
4. After each wave: `.cursor/agents/nord-stage3-reviewer.md`. Then next wave.
5. After wave 4: `.cursor/agents/nord-stage3-final-reviewer.md`. Stop. Do not start stage 4.

## Subagent prompt (implementer)

Give the subagent only: wave id, task ids, `DONE_WHEN`, file list, Global Constraints from the plan. Do not paste the whole conversation.

## Forbidden

AppLocker, WFP, drivers, hosts file modifications, permanent registry keys, killer targeting `NordControl.*`/`Teacher`/`Student`, autostart/Windows Service/MSI installer (Stage 4), cloud signaling, commit unless user asked, `Environment.Exit` on socket errors, internet ping.
