# 471 Check right click in a Release build on Windows

As the developer, I want to check right click in a Release build on Windows, so that users never see the browser menu or dev tools.

Category: test

## What

Run the checklist below on Windows with a Release build and tick each step.

## Why

Job 224 blocks the web view's context menu in script; only a real Release build shows whether it works.

## Notes

- From job 224.
- Split from #308 (one item per OS).
