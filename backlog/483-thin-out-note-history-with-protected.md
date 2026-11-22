# 483 Thin out note history with protected versions

As someone with a long note history, I want old versions thinned out by age while versions I protect are kept, so that history stays useful and small.

Category: feature

## What

`NoteHistory` keeps every version from the last 7 days, one per day for 3 months, then one per week; a version the user protects in the History panel is never removed (and doesn't count against the caps). Before the first thinning the History panel shows a preview of what would go. The 50-version and 25 MB caps stay as an upper bound.

## Why

Today the oldest versions go first once 50 or 25 MB are reached, so a busy week can push out last year. assistant thins by age and protects chosen versions.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).
