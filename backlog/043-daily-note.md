# 043 Daily note

As a Context user, I want one command to open today's note, created with today's appointments and due todos, so that every day starts with a ready page.

## What

Desktop app, plus a CLI command where it fits. A `/today` command opens the note `yyyy-MM-dd.md` for today (read through the injected clock), creating it first with a title, today's appointments and the todos due today. An existing daily note is opened as it is, never overwritten.

## Why

A daily note is a common habit, and building it by hand each morning is busywork. An existing daily note is never overwritten.
