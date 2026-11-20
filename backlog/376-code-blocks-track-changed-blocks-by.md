# 376 Code blocks: track changed blocks by identity, not index

As the user, I want syntax colours in a long note to update only for the code block I edited, so that typing stays quick in notes with many code blocks.

Category: bug

## What

changedCodeBlocks in src/Context.Desktop/editor/src/view.js compares code blocks by index, so inserting or removing a block early in the note marks every later block as changed and re-highlights them. Match blocks by mapped position or content instead, update the changedCodeBlocksJson test hook's contract, and add a test that inserts a block at the top.

## Why

Code review of job 283 (nit 2): only extra work, output is correct.

## Notes

Proposed by job 283-code-blocks-inline (review-code 2).

Proposed by job 283-code-blocks-inline (`factory/jobs/283-code-blocks-inline/`).
