# 110 Narrow the node check in the CI workflow test

As a maintainer, I want the CI workflow test to reject only a real Node setup, so that a harmless comment in ci.yml never fails the suite.

## What

In tests/Context.Tests/CiWorkflowTests.cs the AC-4 test fails if the substring "node" appears anywhere in .github/workflows/ci.yml (case-insensitive). Change it to look only at non-comment lines and reject a Node setup (for example actions/setup-node or a node/npm run line), the way the AC-5 write check was narrowed in job 129.

## Why

Raised by the code and security reviews of job 129: any future comment with words like "nodes" would turn the suite red for no reason. It was left because spec AC-4 asked for the plain substring check.

## Notes

Proposed by job 129-github-actions-ci.

Proposed by job 129-github-actions-ci (`factory/jobs/129-github-actions-ci/`).
