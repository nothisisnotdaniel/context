# 342 Cap the screenshot folder watcher's seen set

As a desktop user, I want the screenshot folder watcher to keep only a bounded list of files it has already seen, so that a folder that churns through many unique file names can't make the app's memory grow without limit.

Category: security

## What

ScreenshotFolderWatcher (src/Context.Desktop/ScreenshotFolderWatcher.cs) keeps a `seen` dictionary with one entry per path for the life of the watched folder. Add a cap with an eviction rule that can't re-import an already imported image (for example drop entries for files that no longer exist, or keep the newest N by write time and skip files older than the oldest kept entry).

## Why

Security review of job 260: low risk in practice, but a folder written by a temp-named tool grows the set without bound; the builder left it because eviction needs a design so a forgotten path isn't imported twice.

## Notes

Proposed by job 260-screenshot-to-inbox (review-security 3).

Proposed by job 260-screenshot-to-inbox (`factory/jobs/260-screenshot-to-inbox/`).
