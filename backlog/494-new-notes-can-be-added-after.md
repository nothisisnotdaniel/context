# 494 New notes can be added after a note is linked to an appointment

As a user, I want to add new notes at any time, so that linking one note to an appointment does not stop me from writing others.

Category: bug

## What

Reported by the user: once there is one note linked with an appointment, adding a new note no longer works. Reproduce it in the desktop app (link a note to an appointment, for example through the appointment page or /meeting, then try to create another note from that appointment's page first, since that is where the user hit it, and also through the notes bar, the palette and capture), find the cause and fix it. Add a test that fails before the fix. Do not guess the cause up front: the report does not say which way of adding a note fails or whether an error is shown, so write down what you found in build.md.

## Why

Adding notes is the core of the app. Being blocked after one meeting note makes the app unusable for the rest of the day.
