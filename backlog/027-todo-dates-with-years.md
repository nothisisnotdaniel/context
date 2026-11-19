# 027 Todo dates with years

As a Context user, I want todos due in another year to show the year, so that I can't mistake them for this year's.

## What

The sidebar shows due dates as `MM-dd` with no year. Show the year when a todo is due in a year other than the current one (e.g. `2027-01-15`), so todos far ahead or long overdue can't be mistaken for this year's.

## Why

`MM-dd` alone makes a todo due next January or one long overdue look like it's due this year.

## Closed 2026-11-19

Already done: the sidebar and `context todos` show the full yyyy-MM-dd, and the only MM-dd display (the old console) was removed by job 145. Closed by job 294 without a build.
