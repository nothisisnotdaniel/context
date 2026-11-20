# 322 Recent notes sidebar: keep the parent name visible on long titles

As a desktop user, I want the parent name of a recent note to stay readable even when its title is long, so that I can tell notes with the same last name part apart in a narrow sidebar.

Category: ui

## What

In the recent notes list (src/Context.Desktop/NotesPanel.razor, button.name.cut), the grey parent name sits inside the same ellipsis button as the title, so a long title cuts the parent off first. Lay the row out so the title shortens before the parent (for example title and parent as separate flex parts, the parent with its own small max width), in light and dark mode, keeping the full name in title.

## Why

UI and UX reviews of job 240: the parent name is what tells same-named notes apart, and it is the first thing to disappear on a long title; the builder left it because a layout change needs its own UI review.

## Notes

Proposed by job 240-recent-notes-sidebar (review-ui 2, review-ux 1).

Proposed by job 240-recent-notes-sidebar (`factory/jobs/240-recent-notes-sidebar/`).
