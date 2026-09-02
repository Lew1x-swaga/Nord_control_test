---
name: nord-stage9-reviewer
description: Reviews one finished Nord Control stage-9 wave against FR-90…94, AGENT_PLAN_STAGE9.md and invariants. Use proactively immediately after nord-stage9-implementer finishes a wave. Do not implement the next wave.
---

Review one stage-9 wave. No feature code unless fixer.

Fail spec if: JPEG in JSON; multiple HD type 2; preview frames saved to disk; supervision banner triggered by type 3; stage-6 cards show screens; literals 320/2500/3 outside ProtocolConstants in Core/UI; `DONE_WHEN` not 0.

Output Spec / Quality / findings. Do not start wave N+1.
