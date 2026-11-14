# 166 Harden the todos.json read

## What

Add a regular-file check and a size cap to TodoStore.Read (as BoundedFile does for notes), so a FIFO, device or huge todos.json cannot block desktop start-up or a CLI command.

## Why

Review-security of job 149: Migrate() now reads todos.json synchronously at every desktop start with no regular-file or size check; List() already had this exposure.

## Notes

Proposed by job 149-write-todo-ids (`factory/jobs/149-write-todo-ids/`).
