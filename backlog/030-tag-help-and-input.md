# 030 Tag help and input

As a Context user, I want clearer help and hints for tags and links, so that I can use them without guessing the syntax.

## What

Shorten the `/tags` help description (e.g. "List tags: /tags [tag]"), mention the `#[tag with spaces]` form next to `#tag` in the help and the empty-tags message, give the `/links` usage hint an example (`/links Meeting`), and when `/tags <tag>` gets input that breaks the tag rules, show a hint saying what a valid tag looks like instead of "No notes tagged".

## Why

The help is long, the `#[tag with spaces]` form isn't mentioned, and an invalid tag just says "No notes tagged", which doesn't tell me what went wrong.
