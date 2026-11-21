# 454 Fix flaky repository settings page test

As the developer, I want the repository settings page test to pass reliably, so that CI and releases stay green.

Category: test

## What

RepositorySettingsActionsTests.SettingsPage_ListsAddsRefusesRemoves (tests/Context.Desktop.Tests/RepositorySettingsActionsTests.cs:166) clicks an element found before a re-render; CI run 44944588222 failed with Bunit UnknownEventHandlerIdException (no event handler with ID 59 for onclick). Re-find each element right before every click (or use cut.InvokeAsync / WaitForElement / WaitForAssertion) so a re-render between Find and Click cannot leave a stale handler id. Check the other Find-then-Click patterns in that file and fix them the same way. Test-only change; no product code.

## Why

CI failed on develop after job 381 merged (9763e72) though the test passed locally; a flaky test blocks every release and hides real failures.
