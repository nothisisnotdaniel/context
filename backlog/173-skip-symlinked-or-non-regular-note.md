# 173 Skip symlinked or non-regular note files

As a Context user, I want note listing to skip .md files that are symlinks or not regular files, so that a link in my notes folder can't make Context read files outside it.

## What

In NoteStore listing (AddLevel and the top level), skip *.md entries that are symlinks or not regular files, the same way linked folders are already skipped. Cover top-level and child notes with tests.

## Why

Raised by the security review of job 152: a child file like Meetings/x.md pointing at ~/.ssh/id_rsa is still listed and read, while symlinked folders are skipped.

## Notes

Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
