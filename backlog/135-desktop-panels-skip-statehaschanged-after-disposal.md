# 135 Desktop panels: skip StateHasChanged after disposal

As a maintainer, I want TagsPanel and LinksPanel to stop rendering once disposed, so that a load finishing after disposal does no needless work.

## What

Guard the post-load StateHasChanged in TagsPanel.razor and LinksPanel.razor (or StateComponent) with a disposed flag or cancellation token.

## Why

Review-code (job 137): a panel disposed mid-load still calls StateHasChanged; Blazor tolerates it, but it is untidy and could hide real disposal bugs.

## Notes

Proposed by job 137-desktop-tags-links (`factory/jobs/137-desktop-tags-links/`).
