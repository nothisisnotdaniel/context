# 520 Copy test reads a rendered code block

Category: test

## What

CSS-only change cannot alter copy text; literal-string test is acceptable. Make the Copy half of AC-4 in CodeBlockBundleTests read a rendered block textContent instead of a literal string.

## Why

review-code: the test passes a literal string to copyText and never reads a rendered block.

## Notes

Proposed by job 438-code-blocks-wrap.6805ce6deb0f40749187f79a95292a72 (`factory/jobs/438-code-blocks-wrap.6805ce6deb0f40749187f79a95292a72/`).
