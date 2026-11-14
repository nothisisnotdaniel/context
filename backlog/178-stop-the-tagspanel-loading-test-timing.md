# 178 Stop the TagsPanel loading test timing out on CI

As a Context maintainer, I want TagsPanelTests.Render_BeforeTheFirstResult_Loading to pass reliably on CI, so that a docs-only merge does not turn develop red.

## What

The test in tests/Context.Desktop.Tests/TagsPanelTests.cs (line 94) holds a Monitor gate while rendering, then calls cut.WaitForAssertion with the default bUnit timeout to see the #work tag. On the GitHub Actions Release run 43618020333 (after job 153, which changed only CLAUDE.md) the wait timed out. Make the test robust: give WaitForAssertion an explicit, generous timeout (as other flake fixes in the suite do) or make the release of the gate and the load deterministic, without weakening what it checks (Loading… while blocked, then the tag list and no status line).

## Why

A timing flake fails the Release workflow on develop for unrelated changes, which blocks factory auto runs and hides real failures.

## Notes

Proposed by job 153-claude-md-tidy after its CI failure.
