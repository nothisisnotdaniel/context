# 369 Work item: read the linked repos in parallel

As the user, I want `context item` and the project page to read all linked repos at the same time, so that a few slow or hanging repos don't make me wait minutes.

Category: tidy-up

## What

WorkItemView (src/Context.Core/WorkItemView.cs) reads each repo's commits one after another with a 10 s git timeout, so 20 hanging repos take up to 200 s. Read them in parallel (bounded, e.g. 4 at a time) and keep the output order stable; add a test with several slow fake repos.

## Why

Security review of job 280 (nit 4): a usability limit, not a security hole.

## Notes

Proposed by job 280-project-page-work-item (review-security 4).

Proposed by job 280-project-page-work-item (`factory/jobs/280-project-page-work-item/`).
