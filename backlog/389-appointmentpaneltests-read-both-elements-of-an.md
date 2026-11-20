# 389 AppointmentPanelTests: read both elements of an order check at the same moment

As a maintainer, I want the AppointmentPanelTests order helper to read both elements from one render, so that a render in between can never make the check flaky.

Category: test

## What

AppointmentPanelTests.Before (tests/Context.Desktop.Tests/AppointmentPanelTests.cs) unwraps Raw(a) and Raw(b) at slightly different moments. A render in between fails loudly on Assert.Same rather than passing on stale markup, so it is safe today. Read both nodes from one snapshot of the rendered markup (for example one Find over the root), even if that makes the call sites async.

## Why

Code review of job 288 (nit 1): acceptable as is, but the builder left it because the fix makes every call site async.

## Notes

Proposed by job 288-ci-appointment-panel-order (review-code 1).

Proposed by job 288-ci-appointment-panel-order (`factory/jobs/288-ci-appointment-panel-order/`).
