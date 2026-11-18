# 295 Typing @[Name] makes a person mention chip right away

As a desktop user writing a note, I want @[Anna Schmidt] to turn into a clickable person mention as soon as I type the closing bracket, so that I can mention and open people without reopening the note.

## What

The note editor (src/Context.Desktop/editor/src/view.js, rules()) has input rules for #tags and [[links]] but none for @[Name]. A typed mention stays plain text until the note is parsed again, so it can't be clicked. Add an input rule that turns '@[Name]' into a mention node when ']' is typed, with the same rules as mentionRule in markdown.js and PersonParser: '@' at the start of a line or after whitespace, text up to the first ']' on the line, normalizePerson for the name, not in code, and no chip when '(' follows. Clicking the new chip opens the person page and creates people/<Name> if it is missing, as a click on a parsed mention does now. Rebuild wwwroot/editor. Saving writes the same markdown as before.

## Why

User report on 2026-11-18: typing @[ in a note does not work.

## Notes

Requested by the user on 2026-11-18. Cause found: rules() in view.js has tagRule and linkRule but no mention rule.
