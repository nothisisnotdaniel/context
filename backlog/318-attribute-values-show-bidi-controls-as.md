# 318 Attribute values show bidi controls as placeholders

As a desktop user, I want attribute values in the Attributes box shown without raw bidi control characters, so that a value can't display in a misleading order.

Category: security

## What

The value field in the Attributes box (NoteEditor.razor) shows DesktopText.OneLine(Value), which keeps bidi embedding, override and isolate controls (U+202A–U+202E, U+2066–U+2069). Show them as the placeholder, as DesktopText.NoteName does for the title since job 235, without changing the saved value unless the user edits it.

## Why

Security review of job 236: the title neutralises bidi controls, the value field doesn't, so the two are inconsistent.

## Notes

Proposed by job 236-attributes-collapsed-box (review-security 2).

Proposed by job 236-attributes-collapsed-box (`factory/jobs/236-attributes-collapsed-box/`).
