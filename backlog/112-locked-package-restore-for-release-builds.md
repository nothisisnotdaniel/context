# 112 Locked package restore for release builds

As a maintainer, I want release builds to restore exactly the tested packages, so that a release never picks up a different transitive dependency.

## What

Add packages.lock.json files and RestoreLockedMode (or --locked-mode) for CI and release builds, with tests that check the setting.

## Why

Security review of job 131: the released package is built from dependencies restored from nuget.org without a lock file, so a release can differ from what the tests ran against.

## Notes

Proposed by job 131-release-workflow (`factory/jobs/131-release-workflow/`).
