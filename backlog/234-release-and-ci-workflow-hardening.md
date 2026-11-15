# 234 Release and CI workflow hardening

As the Context developer, I want the CI and release workflows hardened in one pass, so that releases cannot overlap, leak tokens or publish from the wrong commit.

## What

- Narrow the node check in the CI workflow test (was #110): In tests/Context.Tests/CiWorkflowTests.cs the AC-4 test fails if the substring "node" appears anywhere in .github/workflows/ci.yml (case-insensitive). Change it to look only at non-comment lines and reject a Node setup (for example actions/setup-node or a node/npm run line), the way the AC-5 write check was narrowed in job 129.
- Concurrency and timeout for the release workflow (was #111): Add a concurrency group and timeout-minutes to the release job in .github/workflows/release.yml, and update ReleaseWorkflowTests (it pins the exact key sets).
- Refuse a newline in VERSION in the release scripts (was #118): In .github/workflows/release.yml, make the steps that compute or write VERSION fail when it contains a newline or carriage return. Add a ReleaseWorkflowTests case for it.
- Fetch the earlier desktop package with gh release download (was #119): In the Windows desktop job of release.yml, download the previous release feed and full nupkg with gh release download (GH_TOKEN in env) into the vpk output folder, and drop --token from the vpk call. Update ReleaseWorkflowTests.
- Pin the NuGet source for the desktop packages (was #120): Add package source mapping (nuget.config) that maps Velopack and vpk to nuget.org, or document why it is not needed yet.
- Check the tag and uploaded assets before publishing a release (was #124): In the publish job of .github/workflows/release.yml, before `gh release edit --draft=false`: check the remote tag (git ls-remote or gh api) points at the commit the release was built from, and fail if it was pushed elsewhere; and count only assets with state "uploaded" (select(.state == "uploaded")) when checking the six required files. Add tests in ReleaseWorkflowTests; note that the stub gh can't apply --jq, so test the filter text or move it into a script that can be tested.
- Handle a leftover draft with the same tag (was #125): When the release job creates its draft and a draft for $TAG already exists (for example after a failed release-PR run), either delete the stale draft first or fail with a clear message naming it. Decide which in the spec, and add tests.
- Keep stale drafts out of the Windows delta base (was #126): The Windows Pack step runs `vpk download github --token`, which also sees draft releases (Velopack 1.2.161 has no draft filter), so a stale draft holding Windows assets can become the delta base. Delete stale drafts before the pack, or download the previous release without the token so only published releases are seen. Add tests.

## Why

- #110: Raised by the code and security reviews of job 129: any future comment with words like "nodes" would turn the suite red for no reason. It was left because spec AC-4 asked for the plain substring check.
- #111: Security review of job 131: without them two runs can overlap, for example a develop push while a release PR prune step deletes prereleases, or a re-run of an old run that re-uses its run number and tag.
- #118: Security review of job 133 (nit 4): it is not reachable today, but a newline written to GITHUB_ENV or GITHUB_OUTPUT can set other variables.
- #119: Security review of job 133 (nit 5): a token on a command line can show up in process listings and in tool logs; gh reads it from the environment.
- #120: Security review of job 133 (nit 6): it only matters once a second source exists, but by then nobody remembers to add the mapping.
- #124: Raised by the security review of job 135 (findings 1 and 5): today a tag pushed onto another commit before publish isn't detected, and the asset check is by name only, so a half-uploaded file would pass.
- #125: Raised by the security review of job 135 (finding 2): after a failed run two drafts can share a tag name; it fails closed today, but needs manual clean-up.
- #126: Raised by the security review of job 135 (finding 3): the updater falls back to the full package, so it still works, but deltas can be built from unpublished files.

## Notes

- Merges #110, #111, #118, #119, #120, #124, #125, #126 on 2026-11-16, to save the fixed cost of one job per item.
- Use quality mode, full path, high risk (release workflow and tokens). ReleaseWorkflowTests pins key sets, so update it once for all changes.
- #110: Proposed by job 129-github-actions-ci. Proposed by job 129-github-actions-ci (`factory/jobs/129-github-actions-ci/`).
- #111: Proposed by job 131-release-workflow (`factory/jobs/131-release-workflow/`).
- #118: Proposed by job 133-desktop-builds-auto-update.
- #119: Proposed by job 133-desktop-builds-auto-update.
- #120: Proposed by job 133-desktop-builds-auto-update.
- #124: Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`). Full path, high risk. Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`).
- #125: Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`). Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`).
- #126: Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`). Proposed by job 135-release-draft-publish (`factory/jobs/135-release-draft-publish/`).
