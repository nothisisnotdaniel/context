# 210 Strip bidi and line-separator characters in SafeText

As a Context user, I want text copied from Times or todos into my notes to show as it reads, so that hidden direction or separator characters can't make a line look different from what it says.

## What

In SafeText.Clean, remove or replace Unicode bidi controls (U+202A–U+202E, U+2066–U+2069) and the line and paragraph separators (U+2028, U+2029) as well as other format characters. Check every caller (day end lists, todos, search snippets) still behaves, and add tests. Do the same in DesktopText.OneLine, and include U+200E and U+200F.

## Why

Day end copies Times checkpoint names and todo texts into notes through SafeText, which lets these characters through, so a crafted name could display reversed or split.

## Notes

Proposed by job 171-days-end-daily-note (review-security round 1, nit 2).

Proposed by job 171-days-end-daily-note (`factory/jobs/171-days-end-daily-note/`).

Duplicate #148 (Replace bidi and format characters in shown text, proposed by job 139-desktop-import-export) was merged into this item on 2026-11-15.
