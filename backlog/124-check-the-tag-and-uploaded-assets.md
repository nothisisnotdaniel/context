# 124 Check the tag and uploaded assets before publishing a release

As a maintainer, I want the publish step to confirm the tag still points at the built commit and every asset finished uploading, so that a published release never carries a moved tag or a broken file.

## What

In the publish job of .github/workflows/release.yml, before `gh release edit --draft=false`: check the remote tag (git ls-remote or gh api) points at the commit the release was built from, and fail if it was pushed elsewhere; and count only assets with state "uploaded" (select(.state == "uploaded")) when checking the six required files. Add tests in ReleaseWorkflowTests; note that the stub gh can't apply --jq, so test the filter text or move it into a script that can be tested.

## Why

Raised by the security review of job 135 (findings 1 and 5): today a tag pushed onto another commit before publish isn't detected, and the asset check is by name only, so a half-uploaded file would pass.

## Notes

Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`). Full path, high risk.

Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`).
