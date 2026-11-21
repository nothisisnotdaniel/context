# 475 Check that mermaid diagrams draw on Windows

As the developer, I want to see mermaid diagrams draw on Windows, so that the sandboxed frame works in the real web view.

Category: test

## What

Run the checklist below on Windows in the desktop app and tick each step. This also covers the spacing of the `Drawing diagram…` hint (was #345): fix the CSS if it is off.

## Why

Job 263 runs Mermaid in a sandboxed frame; only the real web view (WKWebView on macOS, WebView2 on Windows) shows whether it loads.

## Notes

- From job 263; folds in #345 (hint spacing, from job 263 review-ui 1).
- Split from #349 (one item per OS).
