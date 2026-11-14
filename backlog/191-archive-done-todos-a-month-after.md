# 191 Archive done todos a month after they are done

As a Context user, I want todos that have been done for over a month moved to an archive file, so that todos.json stays small while nothing I finished is lost.

## What

- Add a todo archive file next to todos.json in the data folder (for example `todos-archive.json`, same format, done todos only).
- Move every todo whose `Done` time is more than 30 days ago from todos.json into the archive, keeping its id, text, due, created and done values.
- Do the move when todos are written (for example when a todo is added or marked done), with the same atomic write as todos.json, and never lose a todo if one of the two writes fails.
- Keep done todos younger than a month in todos.json, as job 159 does today.
- Archived todos still show where done todos are shown (the person page, `context person`, done lists), read from both files; say in the spec whether search and the CLI include them.
- A todos-archive.json that can't be read shows a clear line and is never overwritten.

## Why

Since job 159 done todos stay in todos.json, so the file grows forever and every read gets slower. Moving old done todos to an archive keeps the everyday file small without deleting anything.

## Notes

Added by the user on 2026-11-14 during the auto run. Builds on #169 Todos linked to people (job 159), which keeps done todos in todos.json with a `done` field. Related: bounded todo reads (#115).
