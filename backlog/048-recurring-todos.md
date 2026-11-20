# 048 Recurring todos

As a Context user, I want todos that come back on a schedule, so that routine tasks don't have to be re-entered every time.

## What

Desktop app, plus a CLI command where it fits. `/todo every <weekday|day|month> <text>` adds a todo that, when marked done with `/done`, comes back with its next due date. The sidebar marks recurring todos; old todo files load as before.

## Why

Weekly or monthly chores are easy to forget, and re-adding them is busywork. Old todo files keep working.
