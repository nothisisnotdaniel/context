# 423 Tell the user when a rename cannot update the title line

As a Context user, I want to be told when renaming a note could not update its title line, so that I know why the old title still shows in the editor.

Category: ux

## What

In the desktop app, when a rename succeeds but saving the rewritten # title line fails, show a short notice instead of failing silently; the old # line stays as it is.

## Why

Raised by review-ux in job 353: a failed title save is silent, so the old # line stays and shows in the editor as a heading with no hint why.

## Notes

Proposed by job 353-rename-updates-title (`factory/jobs/353-rename-updates-title/`).
