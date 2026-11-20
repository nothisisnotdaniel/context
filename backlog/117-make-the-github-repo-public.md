# 117 Make the GitHub repo public

As the Context maintainer, I want the nothisisnotdaniel/context repository to be public, so that installed desktop copies can find updates.

## What

Make the repository public in GitHub settings. Before that, check the history for secrets.

## Why

The Windows updater (job 133) reads GitHub releases without a token, so while the repository is private, installed copies never find an update.

## Notes

Proposed by job 133-desktop-builds-auto-update.
