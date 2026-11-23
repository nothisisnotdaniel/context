# 500 Refuse a FIFO or device node as a log file

As a Context user, I want the app to refuse a log file that is not a regular file, so that a FIFO or device node in the logs folder can never block startup or the Settings panel.

Category: security

## What

UpdateLog (.logs/update.log) and CrashLog's session file read and write through PrivateFile. Before reading or appending, check on the open handle that the target is a regular file; if not, skip the read or write (the update check and the app carry on). Cover both logs with one shared check and add tests for a FIFO in place of each file.

## Why

review-security 4 in job 396-version-and-update-log: a FIFO or device node as update.log could block the read. It is inside the user's own 0700 folder, so the risk is low, but the fix belongs in one job for both logs.

## Notes

Left by the builder of job 396-version-and-update-log as a nit.
