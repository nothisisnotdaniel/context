# 019 Survive a bad todo id

As a Context user, I want a todo with an invalid id to be skipped or repaired, so that one bad entry doesn't make all my todos unreadable.

## What

A todo whose stored `id` is not a GUID should be skipped or given a derived id (and reported), instead of making the whole `todos.json` unreadable.

## Why

Today a single non-GUID id breaks the whole `todos.json`. Handling it per todo and reporting it keeps the rest usable.
