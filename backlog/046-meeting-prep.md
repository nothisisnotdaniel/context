# 046 Meeting prep

As a Context user, I want related notes listed under each appointment, so that I have the context for a meeting before it starts.

## What

Desktop app, plus a CLI command where it fits. Under each appointment in `/current`, list (at most 3) notes that share a tag or a title word with the appointment's title or attendees, so the context for the meeting is on screen before it starts. Ignore short and common words; no extra query per word if it can be avoided.

## Why

Finding the right notes before a meeting usually means searching. Matching tags and title words against the appointment shows the likely ones for free.
