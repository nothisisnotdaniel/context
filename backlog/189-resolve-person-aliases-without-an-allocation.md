# 189 Resolve person aliases without an allocation per mention row

As a Context user with many notes, I want the people list and person page to stay fast, so that a large data folder never makes them slow.

## What

In TagIndex.People() and MentionsOf, stop allocating a new AliasResolution for every note_people row via PersonAliases.PersonKey: resolve each distinct key once (or return a struct / cached result). Keep behaviour and tests unchanged; add a test that pins the output for a folder with many mentions.

## Why

Review-code noted that each mention row allocates a new object. That is fine at today's scale but grows with every mention, and changing it later means reshaping the API.

## Notes

Proposed by job 158-person-aliases (review-code round 1, nit 1).

Proposed by job 158-person-aliases (`factory/jobs/158-person-aliases/`).
