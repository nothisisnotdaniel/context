# 492 Import a LogSeq graph

As someone moving from LogSeq, I want to import my whole graph, so that my pages and journals arrive in Context with their links and tags.

Category: feature

## What

The desktop's import gets `Import LogSeq graph`: the user picks the graph folder; its `pages/` become notes (namespaces `a/b` as subpages), `journals/` become daily notes, `[[links]]` and `#tags` stay, `key:: value` properties become attributes, block references and queries stay as text, assets go to attachments. A preview lists what will be created and every name clash; nothing is overwritten; the source folder is never changed.

## Why

assistant plans a LogSeq import. Context imports single .md files only, so moving a graph is slow by hand.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22).
