# 240 Cap the rows read for the decision log

As a Context user, I want the decision list to stay small in memory even when a note holds thousands of Decision: lines, so that the panel and CLI stay fast.

## What

In TagIndex.Decisions, read at most a bounded number of rows (the panel shows at most 100) plus a total count for the "… n more" line; decide whether the CLI keeps printing every decision (for example with its own larger cap or a --all flag). Add tests.

## Why

No cap on rows read today: a huge note full of Decision: lines produces a large list, though each text is cut to 1000 characters.

## Notes

Proposed by job 180-decision-log (review-security round 1, nit 1).

Proposed by job 180-decision-log (`factory/jobs/180-decision-log/`).
