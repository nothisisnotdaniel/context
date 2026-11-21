# 470 Check right click in a Release build on macOS

As the developer, I want to check right click in a Release build on macOS, so that users never see the browser menu or dev tools.

Category: test

## What

Run the checklist below on macOS with a Release build and tick each step.

## Why

Job 224 blocks the web view's context menu in script; only a real Release build shows whether it works.

## Notes

- From job 224.
- Split from #308 (one item per OS).
