# 228 Tags and links panel fixes

As a Context user, I want the tags and links panels to be clear and accessible, so that I can find and follow my notes easily.

## What

- Desktop tags and links: call counter for load tests (was #134): Add a test-visible call counter (or a test seam) on DesktopState so tests for AC-7 (no queries for Sensitive notes) and AC-8 (stale results dropped) assert on query calls instead of side effects.
- Desktop panels: skip StateHasChanged after disposal (was #135): Guard the post-load StateHasChanged in TagsPanel.razor and LinksPanel.razor (or StateComponent) with a disposed flag or cancellation token.
- Links panel: no 'has no linked notes' flash during rename (was #136): While a rename is in progress, keep the previous rows or a neutral loading line in LinksPanel.razor instead of '<old name> has no linked notes.'; adjust AC-6 wording for NoSuchNote if needed.
- Tags and links panels: announce loaded rows (was #137): Add a live announcement (e.g. a polite status line with the row count) when TagsPanel or LinksPanel finishes loading.

## Why

- #134: Review-code (job 137) found AC-7 and AC-8 are proven only indirectly; a regression could slip past the current tests.
- #135: Review-code (job 137): a panel disposed mid-load still calls StateHasChanged; Blazor tolerates it, but it is untidy and could hide real disposal bugs.
- #136: Review-ux (job 137): the brief wrong message is cosmetic but confusing.
- #137: Review-ux (job 137): the status lines are role=status, but row changes have no live announcement.

## Notes

- Merges #134, #135, #136, #137 on 2026-11-16, to save the fixed cost of one job per item.
- Cheap mode is enough: panel text, labels and small layout fixes.
- #134: Proposed by job 137-desktop-tags-links (`factory/jobs/137-desktop-tags-links/`).
- #135: Proposed by job 137-desktop-tags-links (`factory/jobs/137-desktop-tags-links/`).
- #136: Proposed by job 137-desktop-tags-links (`factory/jobs/137-desktop-tags-links/`).
- #137: Proposed by job 137-desktop-tags-links (`factory/jobs/137-desktop-tags-links/`).
