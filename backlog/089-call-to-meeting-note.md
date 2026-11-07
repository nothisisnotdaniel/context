# 089 Call to meeting note

As a Context and Times user, I want a Teams call booked in Times to open the matching meeting note, so that I can take notes the moment the call starts.

## What

When Times books a `Call` checkpoint from Teams call tracking (seen by the desktop app's file watcher in Times' day file, read-only), Context opens the meeting note of the matching appointment (same title, or the appointment running at that time), creating it if there's none. The time itself is booked by Times. A setting turns it off. Builds on Times' Teams call tracking and meeting notes.

## Why

When a call starts there's no time to find or create the note. Times already detects the call; Context only has to react to it.
