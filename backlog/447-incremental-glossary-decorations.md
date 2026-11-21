# 447 Incremental glossary decorations

As a user with long notes, I want the glossary highlighting to update only the changed text, so that typing stays fast.

Category: security

## What

view.js glossaryPlugin rebuilds every decoration on each docChanged transaction. Map the old decorations and rebuild only the changed ranges.

## Why

Job 380's security review: the full rebuild is bounded but could be incremental.

## Notes

Proposed by job 380-glossary-terms (`factory/jobs/380-glossary-terms/`).
