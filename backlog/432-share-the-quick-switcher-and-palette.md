# 432 Share the quick switcher and palette CSS

As a Context developer, I want the quick switcher and the command palette to share their styles, so that the two dialogs do not drift apart.

Category: ui

## What

Move the shared rules of .switcher* and .palette* in app.css into shared classes and use them in both components, with no visible change.

## Why

Raised by the ui review of job 364: the .switcher* rules duplicate the .palette* rules almost line for line.

## Notes

Proposed by job 364-quick-switcher (`factory/jobs/364-quick-switcher/`).
