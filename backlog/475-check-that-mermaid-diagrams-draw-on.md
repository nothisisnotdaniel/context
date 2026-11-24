# 475 Check that mermaid diagrams draw on Windows

As the developer, I want to see mermaid diagrams draw on Windows, so that the sandboxed frame works in the real web view.

Category: test

## What

Run the checklist below on Windows in the desktop app and tick each step. This also covers the spacing of the `Drawing diagram…` hint (was #345): fix the CSS if it is off.

## Checklist

- [ ] Open a note with a ```mermaid flowchart and a sequence diagram: both draw in the light theme.
- [ ] Switch to the dark theme and reopen the note: both draw in dark colours.
- [ ] A broken diagram shows its error text, not a blank box.
- [x] While a diagram draws, the `Drawing diagram…` hint has the same spacing as other editor hints (no overlap, no odd margin). If not, fix it in `app.css`/`editor.css`.
- [ ] In a Debug build, the dev tools console (F12) shows no CSP error: the frame loads `wwwroot/mermaid/mermaid-frame.js` under `script-src 'self'`.
- [x] If any step fails, add a fix item that names the step and what you saw.

## Why

Job 263 runs Mermaid in a sandboxed frame; only the real web view (WKWebView on macOS, WebView2 on Windows) shows whether it loads.

## Notes

- From job 263; folds in #345 (hint spacing, from job 263 review-ui 1).
- Split from #349 (one item per OS).

## Windows verification, 2026-11-24

On develop `29b1b32c`, both diagram types and an ordinary syntax error stayed on “Drawing diagram…” in Light and Dark. The hint cleared its code box by 4 px and left 10 px before the next paragraph, matching the following gap of a separate editor hint. F12 opened the native Debug tools. The frame loaded with HTTP 200 and its declared `script-src 'self'` stayed in force. Its parser received HTTP 404 with HTML for `mermaid-frame.js`. WebView2 reported `net::ERR_BLOCKED_BY_ORB`. An independent stock Photino host with the unmodified normal build's Webroot and resource tables reproduced the same response. Repeat the four open checks after [#517](517-serve-the-bundled-mermaid-renderer-script.md).
