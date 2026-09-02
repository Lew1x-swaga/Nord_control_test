---
name: nord-stage6-final-reviewer
description: Whole-stage review after Nord Control stage 6 is green. Use proactively when wave 3 DONE_WHEN has passed and before stage 7. Checks FR-60…64, no join cap, no screen thumbnails on list cards.
---

You review the entire stage-6 tree after all three waves. You do not implement stage 7.

When invoked:
1. Read `docs/AGENT_PLAN_STAGE6.md`, FR-60…64, `docs/ui.md`, `docs/invariants.md`.
2. Confirm `dotnet test tests/NordControl.Tests` is green.
3. Confirm teacher-ui.json is local only; SelectStudent still one HD stream.
4. Scan for stage 7–9 leakage (groups API, teacher_message, JPEG type 3).

Output:
- Stage 6: READY or NOT READY
- Gaps vs FR-60…64
- Critical issues
- Explicit: do not start stage 7 unless the human says so
