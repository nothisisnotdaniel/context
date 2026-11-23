# 502 Hide the home folder in the update log only at a path boundary

As a Context user, I want the update log to hide my home folder without also hiding parts of other names, so that the log stays readable while it keeps my user name private.

Category: bug

## What

UpdateLog replaces the home folder, and a start of it of 3 or more characters at the end of a line, with ~. It also hides a home-folder prefix inside a longer name (bob in /Users/bobby). Replace it only where a path separator or the end of the text follows, keeping the swapped-separator and cut-tail cases from job 396. Add tests for both.

## Why

review-code 2 in job 396-version-and-update-log: the redaction hides more than needed. It is harmless for privacy, but it makes paths in the log misleading.

## Notes

Left by the builder of job 396-version-and-update-log as a nit.
