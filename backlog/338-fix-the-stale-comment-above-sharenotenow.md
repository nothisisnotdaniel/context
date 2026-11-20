# 338 Fix the stale comment above ShareNoteNow

As a developer, I want the comment above ShareNoteNow to say what the code does, so that nobody thinks Share still writes a plain copy of a sensitive note.

## What

In src/Context.Desktop/DesktopActions.cs (around lines 2884-2885) the comment above ShareNoteNow still calls a sensitive note 'the one deliberate plain copy of a sealed note ... only after the dialog's warning'. Since job 250 round 2, Share refuses sensitive notes; reword the comment to say that, or delete it.

## Why

Code and security reviews of job 250 round 2: the comment describes behaviour that was removed and could mislead a later change to the sensitive-note rule.

## Notes

Proposed by job 250-share-a-note (review-code 1, review-security 1, round 2).

Proposed by job 250-share-a-note (`factory/jobs/250-share-a-note/`).
