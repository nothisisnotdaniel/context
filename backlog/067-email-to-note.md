# 067 Email to note

As a Context user, I want to drop an email file into the desktop app to make a note from it, so that important mail is kept with my notes.

## What

Drop an `.eml` or `.msg` file into the desktop app to create a note from it with the sender, date, subject and the plain-text body (HTML turned into text, no scripts or remote images loaded), the sender and recipients as `@[Full Name]`, and its attachments in `attachments/`. Size cap; the source file is never changed. Builds on people pages and attachments.

## Why

Copying mail into notes by hand loses the sender, date and attachments. Turning HTML into plain text, with no scripts or remote images, keeps it safe.
