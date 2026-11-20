# 026 Recurring appointments

As a Context user, I want recurring appointments to show on every day they occur, so that `/current` shows my real day.

## What

`.ics` events with `RRULE` (plus `RDATE`/`EXDATE`) only show on their first occurrence in `/current`. Expand recurrences (at least daily/weekly/monthly/yearly with `INTERVAL`, `COUNT`, `UNTIL`, `BYDAY`) so they show on every day they occur.

## Why

Today a recurring `.ics` event only shows on its first occurrence, so weekly meetings disappear from every later day.
