# 054 Attachments

As a Context user, I want to drop images and PDFs into a note, so that screenshots and documents live with the notes they belong to.

## What

Drag an image or PDF into a note in the desktop app; the file is copied to a `.attachments/` folder next to the note, inside the note's own hierarchy level (for example `Work/Projects/.attachments/` for a note in `Work/Projects/`), so attachments move and are removed together with their level (size cap, file name cleaned) and linked from the note; images show inline. No scripts from attachments ever run; PDFs open in the system viewer.

## Why

Notes often refer to a screenshot or a document stored somewhere else, where it gets lost. Copying the file in safely, with no scripts ever run, keeps them together. The leading dot keeps the folder out of note lists and the index, like `.history/` (#53).
