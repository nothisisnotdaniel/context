# 320 Crash log link checks: close the check-then-act window

As a desktop user, I want the crash log code to refuse a swapped .logs folder or session file at the moment it writes or opens them, so that a link swapped in after the check can't redirect the write or the launch.

Category: security

## What

CrashLog.WriteSession and DesktopActions.OpenCrashFolder (src/Context.Desktop) check for links and then act, so there is a small window between the check and File.WriteAllText / Launch. If it can be done simply, open the file with options that refuse links (or re-check via the opened handle) so the check and the write are the same step.

## Why

Security review of job 237 (round 2 nit): low impact, since a local process that can swap the folder can already write those files; the reviewer said no change is needed, so this is optional hardening.

## Notes

Proposed by job 237-crash-log-hint (review-security 1, round 2).

Proposed by job 237-crash-log-hint (`factory/jobs/237-crash-log-hint/`).
