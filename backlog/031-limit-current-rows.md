# 031 Limit /current rows

As a Context user, I want `/current` to cap its due todos and tag lookups, so that it stays readable and fast with many todos.

## What

`/current` shows every due todo and runs one database query per distinct tag, with no limit. Cap the Due table (last row `… N more`, like the sidebar) and the number of tags looked up, and build the due list and the tag collection in one loop instead of two.

## Why

Today it shows every due todo and runs a query per tag with no limit. A cap with "… N more" matches the sidebar.
