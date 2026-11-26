# 526 Skip the CI tests for backlog and lessons changes

As the owner, I want CI to skip the tests when a push only changes the backlog or the lessons, so that backlog edits and lessons don't each cost a full test run.

Category: ci

## What

Add the same check as the factory's (factory#85) to this repo's CI workflow in `.github/workflows/`: a first, cheap job that skips the test job, and ends the run green with the log line `only backlog or lessons changed`, when the push or pull request changes only `backlog.md`, files under `backlog/`, or lines inside the `## Lessons` section of `CLAUDE.md`. Every other change runs the tests as now. Keep a run for every pushed commit (no `paths-ignore`), so the factory's CI wait finds a run by commit. Copy the check script from the factory into this repo, since CI here can't read the factory's files, and leave `release.yml` unchanged.

## Why

Every backlog move, new item and lesson the factory commits on develop starts a full test run that tests nothing that changed.

## Notes

Asked for by the user on 2026-11-27, for every project with a CI workflow (Times has none). Use the factory's script and tests from factory#85 as they are.
