# 368 Desktop request folder: tighten an existing .desktop folder and refuse a symlinked lock

As the user, I want Context to make the .desktop folder owner-only even when it already exists, and to refuse a running.lock that is a symlink, so that the desktop hand-off files stay private.

## What

PrivateFile.CreateFolder leaves an existing .desktop folder at its old mode, and PrivateFile.OpenLock follows a symlink at running.lock (src/Context.Core/DesktopRequest.cs, PrivateFile). Tighten an existing .desktop folder to 0700 and refuse a linked lock file with a clear message; add tests for both.

## Why

Security review of job 280 (nit 3): only the same user can plant either, so the risk is low, but it is inconsistent with the owner-only rule.

## Notes

Proposed by job 280-project-page-work-item (review-security 3).

Proposed by job 280-project-page-work-item (`factory/jobs/280-project-page-work-item/`).
