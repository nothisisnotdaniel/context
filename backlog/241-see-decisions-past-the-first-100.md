# 241 See decisions past the first 100

As a Context user, I want a way to see decisions beyond the first 100 in the Decisions panel, so that older decisions aren't out of reach.

Category: feature

## What

In DecisionsPanel.razor, make the "… n more" line actionable (for example a Show more button that loads the next 100, or a hint pointing to the tag filter and the CLI). Update the spec text and tests.

## Why

The "… n more" line has no way to see the rest; only the tag filter narrows the list.

## Notes

Proposed by job 180-decision-log (review-ux round 1, nit 1).

Proposed by job 180-decision-log (`factory/jobs/180-decision-log/`).
