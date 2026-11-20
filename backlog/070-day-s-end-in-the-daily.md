# 070 Day's end in the daily note

As a Context and Times user, I want my worked hours added to the daily note and open todos moved to tomorrow when I leave, so that each day closes itself without extra work.

## What

No dialog. When Times' day file for today gets a leave entry (read-only, same rules as Times bookings in /current), Context adds a "Worked" section to the daily note (arrival, leaving, total and the time per work item, from Times' day file) and moves today's open todos to tomorrow, listing them there under "Moved to tomorrow". Runs once per day (remembered in the settings file); also a button and a CLI command, for any day. Builds on the daily note and on Times' checkpoint data.

## Why

The end of the day is when I'm least likely to tidy up. Using Times' leave entry as the trigger needs no dialog and records the day automatically.
