# 029 Index limits

As a Context user, I want the tag index to skip huge notes and query links efficiently, so that one big note or a big notes folder doesn't slow everything down.

## What

`TagIndex.Sync` reads each changed note whole with no size limit; skip (or only partly read) notes above a sensible size and say so in `/tags`. `TagIndex.Links` reads the whole `note_links` table on every `/links` call; query only the rows for that note and the rows whose target matches it.

## Why

`TagIndex.Sync` reads every changed note whole, and `/links` reads the whole links table each time. Limits and targeted queries keep both fast.
