# 118 Refuse a newline in VERSION in the release scripts

As the Context maintainer, I want the release workflow to reject a VERSION containing a newline, so that a malformed version cannot inject extra lines into later steps.

## What

In .github/workflows/release.yml, make the steps that compute or write VERSION fail when it contains a newline or carriage return. Add a ReleaseWorkflowTests case for it.

## Why

Security review of job 133 (nit 4): it is not reachable today, but a newline written to GITHUB_ENV or GITHUB_OUTPUT can set other variables.

## Notes

Proposed by job 133-desktop-builds-auto-update.
