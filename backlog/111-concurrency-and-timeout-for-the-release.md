# 111 Concurrency and timeout for the release workflow

As a maintainer, I want release runs that never overlap or hang, so that a prune step never races a new prerelease.

## What

Add a concurrency group and timeout-minutes to the release job in .github/workflows/release.yml, and update ReleaseWorkflowTests (it pins the exact key sets).

## Why

Security review of job 131: without them two runs can overlap, for example a develop push while a release PR prune step deletes prereleases, or a re-run of an old run that re-uses its run number and tag.

## Notes

Proposed by job 131-release-workflow (`factory/jobs/131-release-workflow/`).
