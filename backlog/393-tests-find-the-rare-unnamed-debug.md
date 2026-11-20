# 393 Tests: find the rare unnamed Debug failure in Context.Desktop.Tests

As the developer, I want every flaky desktop test found and fixed, so that CI and local runs fail only when something is really broken.

Category: test

## What

During job 292 the first full Debug run of Context.Desktop.Tests had one failure that the quiet test output did not name; it did not come back in 7 later full runs. Run the suite repeatedly with detailed logging (for example --logger 'console;verbosity=normal' and a loop) to catch the failing test, then fix its race the way jobs 288 and 292 did (wait for the panel's load or re-find elements after re-render).

## Why

Code review of job 292 (nit 1): a rare flake that will turn CI red again at some point.

## Notes

Proposed by job 292-ci-people-row-click (review-code 1).

Proposed by job 292-ci-people-row-click (`factory/jobs/292-ci-people-row-click/`).
