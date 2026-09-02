---
name: executing-nord-stage5
description: Use when the user asked for Nord Control school autostart/Windows service/silent install/school memo. Executes docs/AGENT_PLAN_STAGE5.md. Do not start without that request. Fail-open without a teacher session.
---

# Executing Nord Control stage 5

1. If the human did not ask for служба/автозапуск/тихая установка/памятка школе — **stop**.
2. Read `docs/AGENT_PLAN_STAGE5.md` then `docs/subagents.md`.
3. One implementer: `.cursor/agents/nord-stage5-implementer.md`.
4. Waves **1→3**. After wave: `nord-stage5-reviewer`. Then `nord-stage5-final-reviewer`.
5. Prefer user-session autostart if Session-0 cannot capture the desktop. Document the choice.

Forbidden: policies before Join, hiding the process, licenses, cloud, commit unless asked.
