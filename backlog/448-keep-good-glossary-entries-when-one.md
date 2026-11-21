# 448 Keep good glossary entries when one is bad

As a user, I want the editor to keep the good glossary terms when one entry is malformed, so that one bad entry doesn't hide every term.

Category: security

## What

glossary.js readGlossary rejects the whole array when one entry is bad. Skip only the bad entries.

## Why

Job 380's security review: one bad entry rejects the whole list (robustness only).

## Notes

Proposed by job 380-glossary-terms (`factory/jobs/380-glossary-terms/`).
