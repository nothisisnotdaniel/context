# 266 Tag filters: decide on number tags like #1234

As a Context user, I want a filter like `#1234 and not #done` to work the way the saved searches story shows it, so that I can filter by ticket numbers.

## What

Today a note tag must start with a letter, so `#1234` is Invalid in a tag filter (job 207 tests use `#job1234`). Decide whether tags may start with a digit (in Core tag parsing, tag filters and the Tags panel) or whether the help and usage line should say that tags start with a letter and suggest a form such as `#t1234`. Update the tag rules and their tests to match.

## Why

Raised by the ux and code reviews of job 207: the backlog story for saved searches uses `#1234 and not #done` as its example, but it shows the red usage line. If nothing is done, the example the user asked for keeps failing.

## Notes

Proposed by job 207-saved-searches (`factory/jobs/207-saved-searches/`).

- 2026-11-20: My thought on this: work items should look different so we know it’s a work item. Suggestion: #?1234
