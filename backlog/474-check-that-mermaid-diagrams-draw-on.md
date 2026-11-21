# 474 Check that mermaid diagrams draw on macOS

As the developer, I want to see mermaid diagrams draw on macOS, so that the sandboxed frame works in the real web view.

Category: test

## What

Run the checklist below on macOS in the desktop app and tick each step. This also covers the spacing of the `Drawing diagram…` hint (was #345): fix the CSS if it is off.

## Checklist

- [ ] Open a note with a ```mermaid flowchart and a sequence diagram: both draw in the light theme.
- [ ] Switch to the dark theme and reopen the note: both draw in dark colours.
- [ ] A broken diagram shows its error text, not a blank box.
- [ ] While a diagram draws, the `Drawing diagram…` hint has the same spacing as other editor hints (no overlap, no odd margin). If not, fix it in `app.css`/`editor.css`.
- [ ] In a Debug build, the web inspector console shows no CSP error: the frame loads `wwwroot/mermaid/mermaid-frame.js` under `script-src 'self'`.
- [ ] If any step fails, add a fix item that names the step and what you saw.

## Why

Job 263 runs Mermaid in a sandboxed frame; only the real web view (WKWebView on macOS, WebView2 on Windows) shows whether it loads.

## Notes

- From job 263; folds in #345 (hint spacing, from job 263 review-ui 1).
- Split from #349 (one item per OS).
