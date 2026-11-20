# 097 Azure DevOps status

As a Context user, I want work item titles, states and assignees from Azure DevOps shown for `#1234`, so that I see a project's status without opening DevOps.

## What

`#1234` shows the work item's title, state and assignee from Azure DevOps on hover and on the project page. Read-only, with a personal access token kept in the OS key store (never in a file or shown), a short timeout and a cache; off by default. Builds on project pages and OS key store unlock.

## Why

Checking DevOps for every work item mentioned in a note is slow. A read-only, cached, off-by-default lookup with the token in the OS key store keeps it safe.
