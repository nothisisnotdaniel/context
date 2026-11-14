# 172 Return focus to the toolbar after Move or New subpage

As a keyboard user of the desktop app, I want focus back on the notes toolbar after Move, Create subpage or Cancel, so that I don't lose my place.

## What

In src/Context.Desktop/Main.razor, after the Move, Create subpage or Cancel action closes its fields, move focus back to the toolbar button that opened them (or to the new or moved note's row).

## Why

Raised by the ux review of job 152: today focus is dropped after these actions, so keyboard users have to tab back from the start of the page.

## Notes

Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
