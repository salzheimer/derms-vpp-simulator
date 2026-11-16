---
name: run-step
description: Run one approved plan step with a test spec from start to pull request - preflight, the test author, a hard stop for human review of the spec commit, then the implementer. Use when a human asks to run or start a plan step (for example "run p1-s01e"). Never merges. Thin stub; full definition lives at .agents/skills/run-step/SKILL.md.
---

See `.agents/skills/run-step/SKILL.md` for the canonical, full definition of this skill. This file exists only so Claude Code discovers it under `.claude/skills/`; do not duplicate the procedure here — read the canonical file.

Claude Code note: the test author and implementer are the subagents `test-author` and `implementer` (`.claude/agents/`). At the hard stop (step 4), end the turn; continue only on the human's next message.
