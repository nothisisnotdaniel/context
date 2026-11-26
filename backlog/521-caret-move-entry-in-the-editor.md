# 521 Caret-move entry in the editor test hook

As a developer, I want the editor test hook to move the caret, so that the code-block Esc flag clearing on a selection change is covered by a test.

Category: test

## What

Add a caret-move key or step to keySequenceMarkdown (or a sibling hook) and test that moving the caret after Esc in a code block lets Tab indent again.

## Why

The selection-change branch of escapePlugin is only checked by reading the code.

## Notes

Proposed by job 447-tab-indents-code-blocks.917164b0c5024f5d8c1ab24596218497 (`factory/jobs/447-tab-indents-code-blocks.917164b0c5024f5d8c1ab24596218497/`).
