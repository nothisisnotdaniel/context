# 007 Desktop settings

As a Context user, I want a settings page in the desktop app for the calendar link and the sensitive-notes certificate, so that I can set Context up without the console.

## What

A settings page in the desktop app. Set the Exchange calendar link in a masked field, shown only as `CalendarLink.Mask`; the link never lands in logs, URLs or browser storage. Shows the sensitive-notes certificate (name, thumbprint, expiry) once sensitive notes exist. Full path, high risk. Builds on desktop writes.

## Why

The calendar link is a secret (it grants read access to my calendar), so it must stay masked and never land in logs, URLs or browser storage. That's why this takes the full path at high risk.
