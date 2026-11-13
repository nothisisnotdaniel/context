# 130 Refuse a whitespace-only recovery password

As a user, I want a whitespace-only recovery password refused, so that I do not set a recovery file nobody can sensibly reproduce.

## What

Refuse a recovery password that is empty after trimming whitespace.

## Why

Raised by the security review of job 136 (nit 7); a new rule beyond the spec.

## Notes

Proposed by job 136-sensitive-notes-cert (`factory/jobs/136-sensitive-notes-cert/`).
