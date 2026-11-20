# 090 Keep mixed line endings

As a Context user, I want a note with mixed line endings saved without rewriting all its lines, so that saving doesn't cause big, noisy changes.

## What

A note that mixes LF and CRLF line endings is saved with CRLF throughout. Keep each line's own ending, or pick the most common one, and test it.

## Why

Today a note mixing LF and CRLF is saved with CRLF throughout, which changes every line in diffs and sync tools.
