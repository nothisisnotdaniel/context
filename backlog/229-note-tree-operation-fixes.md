# 229 Note tree operation fixes

As a Context user, I want moving, renaming and deleting notes in the tree to be safe and predictable, so that I never lose or misplace a note.

Category: bug

## What

- Return focus to the toolbar after Move or New subpage (was #172): In src/Context.Desktop/Main.razor, after the Move, Create subpage or Cancel action closes its fields, move focus back to the toolbar button that opened them (or to the new or moved note's row).
- Close the check-to-act gap in note Move, Rename, Delete and New subpage (was #174): In NoteStore Move, Rename, Delete and CreateChild, the checks (List, link test, exists test) and the acts (Directory.Move, File.Delete, CreateDirectory) are separate steps. Re-check at the act or use operations that fail when the target changed, and handle the failure with a clear message.
- Remove the empty child folder after a subpage move (was #175): After NoteStore.Move moves a subtree, remove the old parent's child folder when it is now empty (only when it has no other files).
- Merge Find and FindExact in NoteStore (was #176): In src/Context.Core/NoteStore.cs, Find(IReadOnlyList<NoteInfo>, ...) and FindExact(List<Entry>, ...) do the same exact-then-ignoring-case match. Share one implementation, keeping the sensitive-note lookup working.
- Keep deep note trees readable in the sidebar (was #177): In app.css .note-children, reduce the per-level indent at deeper levels or cap it, so deep trees in a narrow sidebar keep room for titles (long titles already end in an ellipsis with the full name as a tooltip).

## Why

- #172: Raised by the ux review of job 152: today focus is dropped after these actions, so keyboard users have to tab back from the start of the page.
- #174: Raised by the security review of job 152: between the check and the act another process or a sync tool can swap in a link or a file.
- #175: Raised by the code review of job 152: the empty folder is harmless because List() ignores it, but it clutters the user's files and sync tools.
- #176: Raised by the code review of job 152: two copies of the same matching rule will diverge the next time one is changed.
- #177: Raised by the ui review of job 152: a 16px indent plus an 18px toggle column per level eats the sidebar width quickly.

## Notes

- Merges #172, #174, #175, #176, #177 on 2026-11-16, to save the fixed cost of one job per item.
- Use quality mode: #174 is a race fix.
- #172: Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
- #174: Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
- #175: Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
- #176: Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
- #177: Proposed by job 152-pages-hierarchy (`factory/jobs/152-pages-hierarchy/`).
