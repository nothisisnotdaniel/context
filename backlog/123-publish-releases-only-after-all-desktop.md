# 123 Publish releases only after all desktop assets are uploaded

As a maintainer, I want the release workflow to publish a release only once every file is attached, so that releases work with GitHub's immutable releases turned on.

## What

In .github/workflows/release.yml the release job runs `gh release create` with only the console nupkg, which publishes the release at once; desktop-macos and desktop-windows then run `gh release upload ... --clobber` into it. With immutable releases on, GitHub refuses that (HTTP 422 'Cannot upload assets to an immutable release'), as happened for v0.0.1-develop.8. Create the release as a draft (`--draft`), let both desktop jobs upload into the draft, and add a final job (needs: both desktop jobs) that publishes it (`gh release edit "$TAG" --draft=false`, keeping --prerelease for develop versions). Run the prune step only after the publish. If a desktop job fails, the draft stays unpublished, so no half-filled release is ever visible or picked up by the Velopack updater. Check that the tag is still created at the right commit (`--target`) when the draft is published, and that the updater (GithubSource) never sees drafts. Update ReleaseWorkflowTests (job order, needs, draft flag, publish step, pinned key sets) and the release section in CLAUDE.md.

## Why

The develop.8 release failed after the repo went public with immutable releases on: it holds only the console package, so the Windows installer and macOS zips are missing and auto-update finds nothing new. Immutable releases also strengthen the unsigned updater's trust model (a published file can't be swapped), so the workflow should work with them on rather than rely on the setting being off.

## Notes

Full path, high risk (release workflow and the updater's feed). Raised from the failed release run for 2b2251e on 2026-11-12.
