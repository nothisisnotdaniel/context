# 367 Commit log: turn off signature checks in git log

As the user, I want Context's git log calls to ignore a repo's signature settings, so that a repo's own config can never make Context run another program when it reads commits.

## What

GitCommitLog (src/Context.Core/GitCommitLog.cs) runs `git log` with a fixed argument list, but a repo's .git/config can set log.showSignature=true plus gpg.program, which runs that program on signed commits. Add `-c log.showSignature=false` (and consider `-c core.fsmonitor=` etc.) to the argument list, and update the AC-6 argument test.

## Why

Security review of job 280 (nit 1): the spec accepted the risk because only folders the user configured are read, but the fix is cheap.

## Notes

Proposed by job 280-project-page-work-item (review-security 1).

Proposed by job 280-project-page-work-item (`factory/jobs/280-project-page-work-item/`).
