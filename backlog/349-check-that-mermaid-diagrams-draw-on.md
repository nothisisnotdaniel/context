# 349 Check that mermaid diagrams draw on macOS and Windows

As the app's owner, I want to see a mermaid diagram actually drawn in the desktop app on macOS and Windows, so that I know the sandboxed frame and its CSP work in the real web views.

Category: test

## What

Run the desktop app on macOS (WKWebView) and Windows (WebView2). Open a note with a ```mermaid block (a flowchart and a sequence diagram), check it draws in light and dark mode, that a broken diagram shows its error, and that the sandboxed frame loads wwwroot/mermaid/mermaid-frame.js under script-src 'self'. Also look at the "Drawing diagram…" hint spacing.

## Why

Job 263 was merged with AC-4 unchecked: tests cannot drive the real web views, so a CSP or sandbox problem would only show here.

## Notes

From job 263-mermaid-diagrams, merged by human with the manual check left for later.
