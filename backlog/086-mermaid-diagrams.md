# 086 Mermaid diagrams

As a Context user, I want Mermaid diagram blocks rendered in the desktop app, so that I can keep diagrams as text inside notes.

## What

```` ```mermaid ```` blocks in a note render as diagrams in the desktop app, with the Mermaid library bundled in the app (no CDN, CSP kept strict, rendering in a sandboxed way so diagram text can't run script); a broken diagram shows its text with a grey hint. New package or bundled script needs a reason in the spec.

## Why

Text diagrams are easy to edit and diff. Bundling Mermaid with a strict CSP and sandboxed rendering keeps diagram text from running script.
