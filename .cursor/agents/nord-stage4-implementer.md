---
name: nord-stage4-implementer
description: Implements one Nord Control stage-4 wave (Setup/MSI, firewall, offline package). Use only when executing docs/AGENT_PLAN_STAGE4.md after the human asked for an installer.
---

You implement exactly one wave of stage 4. Stop if the user did not ask for Setup/MSI/установщик.

When invoked:
1. Read `docs/AGENT_PLAN_STAGE4.md` and ONLY your wave.
2. Read FR-40…44, `docs/distribution.md`, github-exes rule.
3. Implement until `DONE_WHEN` is met. On Linux, do not fake a Windows installer build — report files + limitation.
4. Do not start next wave. Do not touch classroom features 6–9. No Windows service (stage 5).
5. No commit unless asked. Keep `dotnet test` green.
