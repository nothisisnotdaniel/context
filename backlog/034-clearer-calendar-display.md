# 034 Clearer calendar display

As a Context user, I want calendar events and errors shown clearly, so that I can trust what `/current` tells me.

## What

An event that ends exactly at the next midnight shows `23:00-…` (not `23:00-00:00`) like other events that run past today. `Can't read calendar/<file>` says why (too big, can't be opened, not a calendar). Review the `… N more` row wording in the Appointments table, and make a duplicate `STATUS` line first-wins like the other properties.

## Why

An event ending at midnight shows oddly, "Can't read calendar/<file>" doesn't say why, and duplicate `STATUS` lines are handled differently from other properties.
