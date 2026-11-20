# 233 Sensitive-notes recovery rules

As a Context user, I want restoring sensitive notes from a recovery file to check the key and the password properly and explain refusals, so that a restore never reports success for notes it cannot open.

Category: security

## What

- Refuse a recovery file whose key differs from the stored one (was #127): On restore, refuse a recovery file whose key differs from the stored recovery key; on a fresh machine store the key only when it actually opened a note.
- Count a note as restored only when the recovery key opens it (was #128): Count a note as restored only when the recovery key really opens it.
- Refuse a whitespace-only recovery password (was #130): Refuse a recovery password that is empty after trimming whitespace.
- Explain a refused case-only rename and hidden setup buttons (was #132): Improve the message for the refused case-only rename of a sensitive note, and say why the setup buttons are hidden on an unsupported OS.

## Why

- #127: Raised by the security review of job 136 (nit 2); it changes restore behaviour, so it needs its own job.
- #128: Raised by the security review of job 136 (nit 3); it changes restore behaviour.
- #130: Raised by the security review of job 136 (nit 7); a new rule beyond the spec.
- #132: Raised by the UX review of job 136 (nits 1 and 2); changes shown text.

## Notes

- Merges #127, #128, #130, #132 on 2026-11-16, to save the fixed cost of one job per item.
- Use quality mode, full path, high risk (crypto and key handling).
- #127: Proposed by job 136-sensitive-notes-cert (`factory/jobs/136-sensitive-notes-cert/`).
- #128: Proposed by job 136-sensitive-notes-cert (`factory/jobs/136-sensitive-notes-cert/`).
- #130: Proposed by job 136-sensitive-notes-cert (`factory/jobs/136-sensitive-notes-cert/`).
- #132: Proposed by job 136-sensitive-notes-cert (`factory/jobs/136-sensitive-notes-cert/`).
