# 439 Shared async wait helper for desktop tests

As a maintainer of Context, I want one test helper that waits on a condition checked through the bUnit dispatcher without blocking the test thread, so that the desktop tests stop each rolling their own SpinWait with a blocking GetResult.

Category: test

## What

Add a small helper in tests/Context.Desktop.Tests (for example WaitFor.Until(cut, condition, timeout)) that polls a condition through cut.InvokeAsync with an await and a short delay, and use it in ProSettingsTests, ScreenshotActionsTests and OpenRequestWatcherTests in place of SpinWait.SpinUntil with .GetAwaiter().GetResult().

## Why

Three test files busy-spin the test thread on a blocking dispatcher call. It works, but it burns CPU on CI and copies the same pattern each time a flake is fixed; one helper keeps future flake fixes short and consistent.

## Notes

From the code review nit in job 370 (round-1/review-code.md).

Proposed by job 370-ci-pro-focus-flake (`factory/jobs/370-ci-pro-focus-flake/`).
