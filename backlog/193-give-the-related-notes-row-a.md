# 193 Give the related-notes row a proper row header

As a Context user who uses a screen reader, I want the related-notes row under each appointment to say what it is, so that the empty first cell does not read as missing data.

Category: ui

## What

In CurrentPanel.razor, give the .related row's first cell a visually hidden label (for example "Related notes") or mark it as a row header, keeping the time column visually empty. Extend the CurrentPanel tests.

## Why

The related row's first cell is empty with no header, so assistive tech announces a blank cell before the list.

## Notes

Proposed by job 160-meeting-prep-notes (review-ux round 1, nit 2).

Proposed by job 160-meeting-prep-notes (`factory/jobs/160-meeting-prep-notes/`).
