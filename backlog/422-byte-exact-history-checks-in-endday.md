# 422 Byte-exact history checks in EndDay and Merge decision tests

As a Context developer, I want the EndDay and Merge numbering tests to check the history version byte for byte, so that a wrong saved version is caught.

Category: test

## What

In tests/Context.Desktop.Tests/DecisionActionsTests.cs, the EndDay and Merge tests read VersionsOf(..)[0] and assert with Contains. Make them compare the full previous bytes as the other history tests do, building the expected text from the same output format.

## Why

Raised by review-code in job 351: the Contains checks are looser than the byte-for-byte checks elsewhere, so a partly wrong history version would pass.

## Notes

Proposed by job 351-decision-number-history (`factory/jobs/351-decision-number-history/`).
