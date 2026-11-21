# 458 Readable Excalidraw drawings in dark mode

As a user with the dark theme, I want inline Excalidraw drawings to stay readable, so that dark strokes don't vanish on the dark page.

Category: ui

## What

Give `.drawing img` in the editor a light backing (or another theme-aware treatment) so drawings rendered by ExcalidrawSvg, which have a transparent background and #1e1e1e default strokes, stay readable in dark mode. Keep the light theme unchanged.

## Why

Review-ui round 1 of job 384 noted that drawings may be hard to read in dark mode; the spec did not ask for a background, so it was left for its own job.

## Notes

Proposed by job 384-excalidraw-drawings (`factory/jobs/384-excalidraw-drawings/`).
