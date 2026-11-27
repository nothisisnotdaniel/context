# 527 Changelog note per version, shown on first start

As a Context user, I want to see what changed when I open a new version for the first time, so that I find new features and fixes without reading release pages.

Category: feature

## What

Every factory job adds a short entry (one or two plain sentences on what changed for the user) to a changelog file in the repo, under the coming version; a release puts its version number on those entries. The app ships that changelog. On the first start of a version it has not seen before, Context writes one regular note per version (for example "Changelog 1.4.0") into a special Context folder that holds the changelogs of all versions, and opens that note once. They are ordinary notes: searchable and linkable, and a note the user edited is never overwritten. Add the per-job changelog entry to the conventions in Context/CLAUDE.md, so builders write it and reviewers check it.

## Why

Users miss new features and fixes because nothing in the app tells them what changed. Keeping the changelog as notes reuses the app's own search and links and needs no new viewer.

## Notes

Asked for by the user on 2026-11-27. The spec decides where the last seen version is stored and what a first install shows (nothing, or only the current version).
