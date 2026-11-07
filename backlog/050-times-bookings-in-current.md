# 050 Times bookings in /current

As a Context and Times user, I want my Times arrival, time left and open checkpoint in `/current`, so that I see my day in one place.

## What

Desktop app, plus a CLI command where it fits. Read today's Times day file (`TIMES_DATA_DIR` or Times' default data folder, read-only, regular file of capped size, like Context reads `.ics` files) and show the arrival, time left and open checkpoint in `/current`. If there's no Times file or it can't be read, show nothing or a grey line; never write to Times' folder.

## Why

Switching between the two apps to check the time left is a small but constant interruption. Reading is strictly read-only, so Context can never damage Times' data.
