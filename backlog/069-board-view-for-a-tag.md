# 069 Board view for a tag

As a Context user, I want notes and todos shown as columns by a status tag, with drag and drop between them, so that I can track work like on a kanban board.

## What

Notes and todos tagged `#status/<column>` under a chosen tag show as columns in the desktop app; dragging a card to another column changes that tag in the note or todo. Column order is set per board in the settings file. Builds on todos inside notes.

## Why

A board makes the state of a project visible at a glance, and using plain tags keeps the data in the notes rather than in a separate format.
