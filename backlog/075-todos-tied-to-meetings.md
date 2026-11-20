# 075 Todos tied to meetings

As a Context user, I want to tie a todo to a meeting, so that I see it when that meeting comes up and can push it to the next occurrence.

## What

A todo can be tied to an appointment (from `/current`, the meeting note or the appointment's menu in the desktop app; stored by the event's UID plus that occurrence's start, or Outlook's id). It then shows under that appointment in `/current`, on the meeting note and in meeting prep. "Push to next meeting" moves an open todo to the next occurrence of a recurring meeting (its due date becomes that date); for a one-off meeting it offers to pick another appointment. A todo whose meeting was cancelled or moved keeps its date and gets a grey hint. Old todos load as before. Builds on recurring appointments, meeting notes and desktop writes.

## Why

Many todos are "bring up in the next meeting", and they get forgotten until after the meeting.
