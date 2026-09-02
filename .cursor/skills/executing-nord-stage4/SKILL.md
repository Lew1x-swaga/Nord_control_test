---
name: executing-nord-stage4
description: Use when the user asked for Nord Control installer/Setup/MSI/offline package. Executes docs/AGENT_PLAN_STAGE4.md. Do not use for classroom features or Windows service autostart.
---

# Executing Nord Control stage 4

1. If the human did not ask for Setup/MSI/установщик — **stop**.
2. Read `docs/AGENT_PLAN_STAGE4.md` then `docs/subagents.md`.
3. One implementer: `.cursor/agents/nord-stage4-implementer.md`.
4. Waves **1→3**. After wave: `nord-stage4-reviewer`. Then `nord-stage4-final-reviewer`.
5. Do not start stage 5 unless asked. Do not mix with stages 6–9 in the same PR.

Forbidden: AppLocker, WFP, student autostart (stage 5), Store, license HTTP, commit unless asked.
