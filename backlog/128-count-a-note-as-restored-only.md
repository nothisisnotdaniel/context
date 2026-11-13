# 128 Count a note as restored only when the recovery key opens it

As a user restoring sensitive notes, I want the restore count to reflect notes that really opened, so that I can trust the result.

## What

Count a note as restored only when the recovery key really opens it.

## Why

Raised by the security review of job 136 (nit 3); it changes restore behaviour.

## Notes

Proposed by job 136-sensitive-notes-cert (`factory/jobs/136-sensitive-notes-cert/`).
