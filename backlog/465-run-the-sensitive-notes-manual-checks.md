# 465 Run the sensitive-notes manual checks on Windows

As the developer, I want to run the sensitive-notes checks by hand on Windows, so that the real key store is known to work.

Category: test

## What

Run the checklist below on Windows with a throw-away note and a throw-away recovery password, and tick each step.

## Why

The real key stores (MacKeyStore, WindowsKeyStore) have never run; tests use FakeKeyStore only.

## Notes

- From job 136 (factory/jobs/136-sensitive-notes-cert/build.md).
- Split from #133 (one item per OS).
