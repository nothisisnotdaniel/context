# 419 Decision markers: app-numbered !d with a folder prefix, #!d references with a picker

As a Context user, I want decisions marked with !d and numbered only by the app under a prefix for my data folder, and referenced with #!d through a picker, so that numbers never clash, are never typed wrong, and stay valid when notes are shared.

Category: feature

## What

Rework the decision syntax from job 343 (#255):

1. **Marker.** `!d` followed by a space at the start of a paragraph (after an optional list marker or indent) defines a decision; `!done` and other words do not. On save the app writes `!d <prefix>-<n>` (for example `!d k7qm-1234`). Any number or prefix the user types after `!d` is removed and replaced by one the app assigns, unless it is exactly the number this paragraph already had. Every write path numbers markers, as job 343 does now.
2. **Folder prefix.** On first start each data folder gets a random 4-character base-32 id plus a folder GUID, stored in a file inside the data folder (so every device syncing the folder shares it). Numbers are unique within the prefix. Your own decisions are shown as `Decision 1234`; decisions with another prefix show it (`Decision ab12-1234`).
3. **Prefix clash.** Notes from another folder carry that folder GUID once in front matter. If a prefix matches yours but the GUID does not, warn once and give that folder a local display alias (kept in the index only; files are not changed).
4. **Copies.** Same prefix-number and same paragraph text in several notes is one decision (shown once in the log, with each place it appears). Same prefix-number with different text: the note that had it first keeps it; the others get a new number on their next save. Editing the original keeps its number.
5. **References.** `#!d1234` (own prefix) or `#!dab12-1234` anywhere except the start of a decision paragraph is a reference; typing numbers there is allowed. Typing `#!d` in the editor opens a picker that searches decisions by headline and number and inserts the reference. Each decision in the log and in the note has a Copy reference action. Unknown numbers show as plain text with a hint, as today.
6. **Migration.** Existing `#!d<n>` decision markers become `!d <prefix>-<n>` with the same number, once, saved with a history version. Existing `#!d<n>` references keep working unchanged. Parser version bump.
7. CLI, read view, log, jump and highlight work as after job 343. Light and dark mode.

## Why

Job 343 lets users type decision numbers, which can use up the counter or clash, and plain numbers will collide as soon as notes are shared between people. App-only numbering with a folder prefix keeps every number unique and stable, and the picker makes references easy without remembering numbers. Without this, sharing notes later needs a migration over every note.

## Notes

Requested by the user on 2026-11-21 after job 343. Decided: numbers stay in the text (no hidden GUIDs, so numbers are the same on every device); random prefix plus folder GUID with a local alias on clash (not user-chosen); !d defines, #!d references. Replaces follow-up #417 (typed numbers above the counter). Related: #416 (history version on numbering), #418 (numbering on CLI and merges, which this item covers). Sharing notes itself is out of scope; this only fixes the format and the clash handling. High risk: data format and migration.
