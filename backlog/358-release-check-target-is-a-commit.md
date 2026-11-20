# 358 Release: check TARGET is a commit sha in the version checks

As the Context developer, I want the release workflow to refuse a TARGET that isn't a plain commit sha, so that a malformed value can never reach a later step.

## What

The Check version steps in .github/workflows/release.yml refuse a line break in VERSION and TAG but not in TARGET. Add a check that TARGET is only hex characters (case "$TARGET" in *[!0-9a-f]*) and has the length of a commit sha, in each Check version step and in publish, and update ReleaseWorkflowTests (step hashes and exit counts).

## Why

Security review of job 272 (nit 2): TARGET comes from the release job's output, which the Version step already checks, so this is defence in depth; without it a later change to how TARGET is set could pass an unchecked value along.

## Notes

Proposed by job 272-release-ci-hardening (review-security 2).

Proposed by job 272-release-ci-hardening (`factory/jobs/272-release-ci-hardening/`).
