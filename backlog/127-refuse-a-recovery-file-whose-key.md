# 127 Refuse a recovery file whose key differs from the stored one

As a user restoring sensitive notes, I want a recovery file with the wrong key to be refused, so that I am not misled into thinking my notes are restorable.

## What

On restore, refuse a recovery file whose key differs from the stored recovery key; on a fresh machine store the key only when it actually opened a note.

## Why

Raised by the security review of job 136 (nit 2); it changes restore behaviour, so it needs its own job.

## Notes

Proposed by job 136-sensitive-notes-cert (`factory/jobs/136-sensitive-notes-cert/`).
