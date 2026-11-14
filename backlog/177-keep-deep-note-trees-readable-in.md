# 177 Keep deep note trees readable in the sidebar

As a Context user with deep page trees, I want deep levels to keep enough width for titles, so that I can still read note names several levels down.

## What

In app.css .note-children, reduce the per-level indent at deeper levels or cap it, so deep trees in a narrow sidebar keep room for titles (long titles already end in an ellipsis with the full name as a tooltip).

## Why

Raised by the ui review of job 152: a 16px indent plus an 18px toggle column per level eats the sidebar width quickly.

## Notes

Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
