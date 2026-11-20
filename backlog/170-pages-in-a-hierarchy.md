# 170 Pages in a hierarchy

## What

Desktop app, plus a CLI command where it fits. A note can have child notes, to any depth (for example Meetings > Meeting 1, Meeting 2, and under those more pages). Stored as plain files: the children of `Meetings.md` live in a `Meetings/` folder next to it, so the Markdown files stay readable and the depth has no fixed limit. The notes list shows the tree with expand/collapse, an open note shows its path as a breadcrumb, and a note offers "New subpage". Renaming or moving a page moves its children with it; deleting a page with children asks first. Search, tags, links and the index see child notes like any other; on the command line a child is named by its path (`Meetings/Meeting 1`). Existing top-level notes load as before. Sensitive notes and name lookup keep their rules for child notes. A data-format change: high risk.

## Why

Related pages (all meetings, all notes on one project) belong together; a flat list gets long and hides that structure, and a fixed number of levels never fits every topic.
