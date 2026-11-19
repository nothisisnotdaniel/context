# 360 Test the full removed-option sentence in CLAUDE.md

As the Context developer, I want the CLAUDE.md test to check the whole sentence about removed secret options, so that the doc can't drift from what the CLI does.

## What

The ClaudeMd test checks only the phrase "the removed secret option is a usage error in any position". Extend it to also check "ignoring case", or reword the sentence so the checked phrase covers everything it states.

## Why

UX review of job 273 (nit 1): the reviewer said no change is needed today, but the parts of the sentence the test does not check could go stale unnoticed.

## Notes

Proposed by job 273-refuse-removed-secret-options (review-ux 1).

Proposed by job 273-refuse-removed-secret-options (`factory/jobs/273-refuse-removed-secret-options/`).
