# 136 Links panel: no 'has no linked notes' flash during rename

As a desktop user, I want the links panel not to briefly say a note has no links while I rename it, so that I am not misled.

## What

While a rename is in progress, keep the previous rows or a neutral loading line in LinksPanel.razor instead of '<old name> has no linked notes.'; adjust AC-6 wording for NoSuchNote if needed.

## Why

Review-ux (job 137): the brief wrong message is cosmetic but confusing.

## Notes

Proposed by job 137-desktop-tags-links (`factory/jobs/137-desktop-tags-links/`).
