# 255 Decisions longer than one line: a paragraph or a whole note

As a Context user, I want a decision to hold its reasoning, a paragraph or a whole note, so that the decision log shows why something was decided and not just a one-line headline.

## What

Extend the decision log (DecisionParser, TagIndex note_decisions, DecisionsView, DecisionsPanel.razor, the `context decisions` command): (1) Paragraph: a `Decision:` line keeps working as today, and the lines that directly follow it (up to the next blank line, heading, list item at the same or a lower indent, or fence) belong to the same decision; the spec fixes the exact rule, including lists under the decision line. (2) Whole note: a note marked as a decision (the spec picks one marker, for example a `#decision` tag or a `type: decision` attribute) is one decision whose headline is the note title and whose text is the note body. (3) The log shows each decision's headline (the `Decision:` line's text, or the note title) as today, with its date and people, and lets the user expand it to read the full text; the CLI shows the headline by default and the full text with a flag. (4) The text length cap, sensitive-note exclusion and fence handling stay as today; a parser version bump re-reads every note. Single-line decisions written today look and behave exactly as before. Light and dark mode.

## Why

Today a decision is only the rest of one `Decision:` line, so the reasons, options and consequences written right below it are lost from the log, and a note that is entirely about one decision does not show at all.

## Notes

Requested by the user on 2026-11-17. Builds on job 180 (#60, decision log); related open items #239 (wider text column) and #241 (decisions past the first 100).
