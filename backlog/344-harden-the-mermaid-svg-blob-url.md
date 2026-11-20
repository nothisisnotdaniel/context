# 344 Harden the mermaid SVG blob: URL

As a desktop user, I want rendered diagrams to be shown without a navigable blob: URL, so that a crafted diagram can never be opened as a page of its own in the app's web view.

Category: security

## What

Mermaid diagrams are shown from a blob: SVG URL. Switch to a data: URL, or block navigation to blob: URLs in the desktop host (WKWebView and WebView2), and add a test that a blob: navigation is refused.

## Why

Security review of job 263: nothing navigates to the blob today, so this is hardening beyond the spec; without it a later change that lets the SVG be opened could run it outside the sandbox.

## Notes

Proposed by job 263-mermaid-diagrams (review-security 1).

Proposed by job 263-mermaid-diagrams (`factory/jobs/263-mermaid-diagrams/`).
