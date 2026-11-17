# 255 Numbered #!d decisions: a marked paragraph, referenced as #!d1234

As a Context user, I want to mark a paragraph as a decision with a short `#!d` marker that gets a number, so that the decision log shows why something was decided, where it was decided, and I can reference and jump to any decision by its number.

## What

Replace the decision syntax and extend the decision log (DecisionParser, TagIndex note_decisions, DecisionsView, DecisionsPanel.razor, NoteEditor/editor.js, the `context decisions` command):

1. **Marker.** `#!d` at the start of a paragraph (after optional list marker or indent; the spec fixes the exact rule) marks that paragraph as a decision. The whole paragraph, up to the next blank line, heading or fence, is the decision's text; its first line (without the marker) is the headline. Outside fences only, like tags.
2. **Numbering written into the note.** When a plain note is saved (desktop editor, and any other path the app writes notes through), every bare `#!d` that marks a decision becomes `#!d<n>` with the next free number, unique across all notes (for example `#!d1234`). The number then lives in the text, so it survives renames, moves and re-indexing. The spec decides where the counter lives, how a number already in use twice (for example a copied note) is handled, and that notes the app does not write (edited outside the app) are numbered the next time the app saves them, or listed as unnumbered until then. A bare `#!d` in a sensitive note is never shown in the log, as today.
3. **References.** `#!d1234` anywhere else than at the start of a decision paragraph is a reference to decision 1234. In the editor and read view it renders as a link (showing the number, with the decision's headline as a tooltip); clicking it opens the note at that decision. An unknown number is shown as plain text with a hint.
4. **Log.** Each entry shows the number (`#!d1234`), the headline, the title of the note it was decided in (the page title), the date and the people, as today; it can be expanded to read the whole paragraph. The CLI shows number, headline and note title, the full text with a flag, and `context decisions 1234` (or similar, the spec picks) shows one decision.
5. **Jump.** Clicking a decision in the log (or a reference) opens its note and scrolls to and briefly highlights the paragraph with that `#!d<n>` marker.
6. **Old syntax removed.** `Decision:` lines are no longer decisions: they become plain text and leave the log; nothing is migrated (the user converts the ones they want by hand). Remove the old parsing, its tests and docs. Parser version bump so every note is re-read.

Text length cap, sensitive-note exclusion and fence handling stay as today. Light and dark mode.

## Why

Today a decision is only the rest of one `Decision:` line, so its reasons are lost, the log doesn't say clearly where it was decided, and there is no way to point at a decision from another note. A short numbered marker fixes all three.

## Notes

Requested by the user on 2026-11-17 (merged into this item: paragraph decisions, `#!d` shorthand, numbered references, page title + number in the log, click jumps to the paragraph, drop `Decision:` without migration). The whole-note form from the first version of this item is dropped in favour of the marker. Builds on job 180 (#60, decision log); related open items #239 (wider text column) and #241 (decisions past the first 100). Must not clash with tags: `#!d` must not be read as a tag, link or heading (the spec checks `TagParser` and the other parsers).
