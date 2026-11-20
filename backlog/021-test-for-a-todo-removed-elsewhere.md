# 021 Test for a todo removed elsewhere

As a maintainer, I want a test for `/done` on a todo that was removed elsewhere, so that the "There is no todo n" path stays covered.

## What

Add a test for the `TodoCommands` branch where the numbered todo was removed elsewhere before `Complete(id)`, which shows "There is no todo n".

## Why

That branch of `TodoCommands` has no test, so a change could break it without anyone noticing.
