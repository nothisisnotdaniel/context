# 040 View doc tidy-ups

As a maintainer, I want the view docs to describe their values precisely, so that front ends don't assume things that aren't true.

## What

The `CurrentView.Reason` doc says "a fixed text or `HTTP <code>`" (not always a fixed text), so front ends don't assume a closed list; split the `CurrentView.Status` summary into a `<list>` with one entry per value; add a test that pins what `CalendarFeedResult` holds for NotConfigured (no text, no time, no reason).

## Why

`CurrentView.Reason` isn't always a fixed text, the `Status` summary should list its values, and the NotConfigured result deserves a pinning test.
