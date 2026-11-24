# 167 Check the first todos.json id migration

Category: test

## What

Back up <root>/todos.json, start the new desktop build once, and check that every todo is still there with an id field added and nothing else lost.

## Why

Job 149 rewrites the real todos.json once, without a prompt; no test can run against your own file.

## Windows verification with synthetic data

On 2026-11-24, the unmodified pre-ID Context terminal build at `7815cf9` created six sample todos through its normal `/todo` command. A backup was kept before starting the Windows desktop Release build at `29b1b32c` on a separate copy. All six entries retained their file order and exact `text`, `due` and `created` values. Each gained only `id`, a distinct non-empty GUID matching an independent derivation. The desktop displayed the todos after both starts. On the second start the file's bytes, last-write time and Windows file identity stayed unchanged.

The item remains open for the owner's original file. The owner can compare a local pre-migration backup with the migrated file without sharing any todo text. No owner data was used in this check.
