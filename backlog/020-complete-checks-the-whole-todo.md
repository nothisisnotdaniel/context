# 020 Complete checks the whole todo

As a Context user, I want completing a todo to check that it's still the one I saw, so that I never tick off a todo that was changed elsewhere.

## What

`TodoStore.Complete` removes only when the stored todo still matches the shown `TodoItem` (id, text, due), not only its id, and refuses with the usual changed text otherwise.

## Why

`TodoStore.Complete` matches by id only, so a todo edited in the meantime would still be removed. Comparing text and due date as well, and refusing otherwise, prevents that.
