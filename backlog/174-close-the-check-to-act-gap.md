# 174 Close the check-to-act gap in note Move, Rename, Delete and New subpage

As a Context user, I want note moves, renames, deletes and new subpages to act safely even if the folder changes at the same moment, so that a race can't move or delete the wrong files.

## What

In NoteStore Move, Rename, Delete and CreateChild, the checks (List, link test, exists test) and the acts (Directory.Move, File.Delete, CreateDirectory) are separate steps. Re-check at the act or use operations that fail when the target changed, and handle the failure with a clear message.

## Why

Raised by the security review of job 152: between the check and the act another process or a sync tool can swap in a link or a file.

## Notes

Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
