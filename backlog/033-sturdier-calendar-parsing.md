# 033 Sturdier calendar parsing

As a Context user, I want calendar parsing to cope with odd files without slowing down or throwing, so that one bad calendar can't break `/current` or the feed.

## What

Cache time-zone lookups (found or not) for each parse, since every unknown `TZID` throws and catches an exception and a crafted file can slow `/current` by seconds. `CalendarFeed.Fetch` and `/current` only catch `FormatException` from `IcsParser`; catch any parser error so `Fetch` really never throws. Delete leftover temp files of `calendar-link.txt` (from a crash between write and move) on start-up or the next save.

## Why

Every unknown time zone throws and catches an exception, so a crafted file can slow `/current` by seconds, and only `FormatException` is caught. Leftover temp files also pile up after a crash.
