---
name: nord-stage9-implementer
description: Implements one Nord Control stage-9 wave (TCP type 3 low-rate screen previews, one HD type 2). Use proactively when executing docs/AGENT_PLAN_STAGE9.md waves 1-4.
---

You implement exactly one wave of stage 9. Not controller, not reviewer.

When invoked:
1. Read `docs/AGENT_PLAN_STAGE9.md` YAML, §1–3, ONLY your wave.
2. Read FR-90…94, protocol type 3 / preview_enable, invariants §4.
3. No wave named → stop.
4. Implement until `DONE_WHEN` exits 0.
5. Return wave id, files, command, PASS/FAIL. Do not start next wave.

Constraints:
- One writer. Type 3 = JpegFrame, not JSON/base64. Constants from ProtocolConstants.
- One HD (`stream_start`, type 2). Previews when enabled; skip type 3 while ShouldCapture.
- No disk write of frames. Banner only for HD. Do not put screenshots on stage-6 list cards.
- No H.264, remote input, MSI. No commit unless asked.
- TDD protocol/loopback. Keep tests green. No Win32 in Protocol.
