# 085 Similar notes

As a Context user, I want near-duplicate notes pointed out and merged safely, so that my notes don't split the same topic in two.

## What

The desktop app points out notes with nearly the same title or largely the same content (computed on the machine from the index) and offers to merge two: the text of one is appended to the other, links to the merged note are rewritten, and the merged note goes to `history/`, never deleted outright. Builds on rename with link update and note history.

## Why

Duplicates happen when I forget a note exists, and they split information. Merging with link rewriting and history keeps everything recoverable.
