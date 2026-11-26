# 519 Check code block wrapping in the desktop app

Category: ui

## What

Manual desktop layout check, cannot be done in code: long lines wrap with no scroll bar, wrapped lines keep their indentation, a long URL, the header and Copy button on the top edge, and a Mermaid block.

## Why

review-ui: real layout cannot run in Jint, so the wrap from #504 is only checked by CSS tests.

## Notes

Proposed by job 438-code-blocks-wrap.6805ce6deb0f40749187f79a95292a72 (`factory/jobs/438-code-blocks-wrap.6805ce6deb0f40749187f79a95292a72/`).
