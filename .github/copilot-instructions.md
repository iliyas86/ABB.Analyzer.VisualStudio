# Copilot Instructions

## Project Guidelines
- QUACK CLI: `check --json` evaluates the local quality gate (blocked/findings/testGuidance) but never runs AI (aiReview is always null). `watch --once --json` performs the AI review (aiReview/risk/filesChanged) but does not evaluate the local gate. Both outputs must be combined (not one discarding the other) to get a complete dashboard result.