# 042 Label shared-tag rows

As a Context user, I want rows linked only by shared tags to say "shared tag" in `/links`, so that a blank cell doesn't look like missing data.

## What

When the `/links` table shows the Link column, rows linked only by shared tags have a blank cell; show e.g. `shared tag` instead.

## Why

A blank Link cell is confusing next to rows that do have a link.
