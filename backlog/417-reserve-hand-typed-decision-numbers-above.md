# 417 Reserve hand-typed decision numbers above the counter

As a Context user, I want a decision number I typed by hand to be reserved, so that no other note can be given the same number later.

Category: security

## What

When the index sees a #!d<n> above the decision counter that the counter steps over, record it as reserved or owned so a later reservation never hands out that number to another note.

## Why

Raised by the security review of job 343 round 2: typed numbers above the counter are skipped but not reserved, so two notes could end up with the same number.

## Notes

Proposed by job 343-numbered-decisions.
