# 044 Note templates

As a Context user, I want to create notes from templates with date, time and title filled in, so that recurring kinds of notes start with the right structure.

## What

Desktop app, plus a CLI command where it fits. `/new <name> --template <template>` creates the note from `<root>/templates/<template>.md`, filling `{{date}}`, `{{time}}` and `{{title}}`; unknown placeholders stay as they are. A missing template shows a hint that lists the templates there are. Works with encryption on.

## Why

Meeting notes, reviews and similar notes have the same skeleton each time. Templates in a folder are simple to manage and need no new format.
