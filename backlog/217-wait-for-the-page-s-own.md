# 217 Wait for the page's own loads in MainTests.Open

As the Context developer, I want every MainTests helper that opens a note to wait until the page's own loads have rendered, so that a future test using the simple helper can't flake the way the breadcrumb test did.

## What

Make MainTests.Open wait until the links panel (and any other Task.Run load the page starts) shows no Loading… before returning, like MainTests.OpenFull does since job 175, or merge the two helpers. Keep every test's checks as they are and run the desktop suite several times in parallel to confirm.

## Why

Job 175 fixed the breadcrumb flake in OpenFull only; Open still returns before the links panel has loaded, so a test that compares elements after Open would flake the same way.

## Notes

Proposed by job 175-flaky-desktop-tests (review-code round 1, nit 1).

Proposed by job 175-flaky-desktop-tests (`factory/jobs/175-flaky-desktop-tests/`).

Merged on 2026-11-15: #179 (MainTests.Breadcrumb_ChildOnly_AncestorsAsButtons_CurrentAsText fails now and then in a full run). It opens its page through MainTests.Open, so this fix should cover it. Check that test in the repeated runs.
