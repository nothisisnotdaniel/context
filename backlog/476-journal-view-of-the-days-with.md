# 476 Journal view of the days with content

As someone who writes every day, I want one scrolling journal of the days that have notes or appointments, so that I can read back through my work without opening day after day.

Category: feature

## What

A desktop page (palette `/journal`) that lists the days, newest first, that have a daily note with text beyond its title, a meeting note, or an appointment. Each day shows its date, the daily note's text (read-only, with an Open button) and the day's appointments and meeting notes as links. Empty days are left out. It loads a bounded number of days and more on scroll.

## Why

Context has the daily note, the weekly review and the done log, but no way to read the days in a row. assistant's journal view shows only days with content, which makes looking back fast.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).

## Known issue accepted on 25 November 2026

The operator accepted the Journal implementation reviewed in round 20 at `dfc9b98a7b11`. When an earlier plaintext version remains after **Mark sensitive**, the red warning can display two adjacent spaces in a recovery path as one. Open **Recovery paths** to select the exact folder or filename. The operator considers this case unlikely and deferred the display correction to [Context #519](519-preserve-consecutive-spaces-in-recovery-warnings.md).
