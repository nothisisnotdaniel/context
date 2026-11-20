# 340 Briefing links for note names with brackets

As a Context user, I want the morning briefing to link a note whose name contains [ or ] correctly, so that clicking it opens that note instead of a broken or wrong link.

Category: bug

## What

src/Context.Core/Briefing.cs (around line 211) writes note names as [[name]] links; a name containing [ or ] gives a misleading [[ ]] link. Escape or otherwise render such names so the link targets the right note, the same way in every briefing section that links notes (To review and the existing ones).

## Why

Security review of job 256 (rounds 1 and 2): not exploitable, since it is the user's own text, but the link is wrong; the builder left it because the other briefing sections behave the same way.

## Notes

Proposed by job 256-review-dates (review-security 1).

Proposed by job 256-review-dates (`factory/jobs/256-review-dates/`).
