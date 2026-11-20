# 402 Throttle repeated show requests

As the user, I want Context to ignore show requests that arrive too fast, so that another program can't keep flashing or stealing focus with the window.

Category: bug

## What

In the desktop app, drop a show request from .desktop/open-request when the last one was handled less than a second ago (OpenRequestWatcher ShowRequested / BringToFront in Program.cs). Add a test that two show requests within a second bring the window forward once.

## Why

Raised by the security review of job 315: any same-user process that can write <root>/.desktop/ can raise the window as often as it likes. The 0700 folder limits who can, but nothing limits how often.

## Notes

Proposed by job 315-second-copy-no-crash.
