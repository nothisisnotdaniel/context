# 180 Force a fresh locked restore on CI

As a maintainer, I want the CI restore to re-check the lock files even when obj/ is left over, so that a hand-edited lock file can never slip through on a runner that keeps its workspace.

## What

Add --force to the locked dotnet restore in ci.yml (and the release restore if it has one), so NuGet re-validates packages.lock.json instead of taking a no-op restore from an existing obj/. Update the workflow tests to require it.

## Why

Security review of job 155: a no-op restore without --force does not re-check a hand-edited lock file. GitHub-hosted runners start without obj/, so it does not matter today, but a self-hosted runner with a persistent workspace would lose the lock-file protection.

## Notes

Proposed by job 155-locked-package-restore (`factory/jobs/155-locked-package-restore/`).
