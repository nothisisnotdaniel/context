# 060 Decision log

As a Context user, I want lines starting with `Decision:` collected into one view, so that I can find what was decided, when and with whom.

## What

A line starting with `Decision:` in any note is collected (in the index, like tags) into a Decisions view with its date, the note it came from and the people mentioned on that line, newest first and filterable by tag. A CLI command lists them too.

## Why

Decisions are scattered across meeting notes and hard to find later. Collecting them like tags costs nothing extra to write.

## Notes

Duplicate #104 (Decision log) was merged into this item on 2026-11-15. #104 also had a decision template (context, decision, consequences) created by `context decision new`, a `--item <number>` filter once work item references (#101) exist, and `--export <file>` for a shareable Markdown decision register (escaped, no scripts). These are optional extras for a later item, not part of this one.
