# 167 Check the first todos.json id migration

## What

Back up <root>/todos.json, start the new desktop build once, and check that every todo is still there with an id field added and nothing else lost.

## Why

Job 149 rewrites the real todos.json once, without a prompt; no test can run against your own file.
