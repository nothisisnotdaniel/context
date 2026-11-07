# 016 Release workflow

As a maintainer, I want the same release workflow Times has, so that Context gets prereleases and checked releases without manual steps.

## What

The same release flow as Times (`.github/workflows/release.yml` and its scripts): a push to `develop` makes a prerelease `X.Y.(Z+1)-develop.<run number>`, a merged `develop` -> `main` pull request titled `release vX.Y.Z` (or a pushed `vX.Y.Z` tag on `main`) tests, packs with a checked SemVer `-p:Version`, smoke-installs and creates the GitHub release; a push that changes only `backlog.md` runs nothing, older `-develop` prereleases are pruned after a release, actions are pinned to full commit SHAs, and workflow tests read `release.yml` through a small YAML reader like Times' `WorkflowYaml`. The spec decides what Context ships (at least the console app; signed desktop builds stay a separate line) and the release is documented in CLAUDE.md, so `/factory release --project Context` works.

## Why

Times already has a tested flow for prereleases from `develop` and releases from a `release vX.Y.Z` PR. Reusing it gives Context the same reliable versioning and smoke-tested packages.
