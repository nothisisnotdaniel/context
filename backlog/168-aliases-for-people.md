# 168 Aliases for people

## What

Desktop app, plus a CLI command where it fits. A person page has an Aliases field where I set shorthands for that person (for example `Anna` or `AS` for Anna Schmidt), stored in the person note itself. `@[Anna]` in a note then links to the same person as `@[Anna Schmidt]`, matched with the same rules as full names (trimmed, whitespace runs become one space, ignoring case). An alias used by two people, or equal to another person's full name, is not resolved: it shows as an unresolved mention with a hint, and the person page warns about the clash. Changing aliases re-links existing mentions on the next index (a parser version bump if needed).

## Why

Typing full names every time is slow; with shorthands I can mention people as fast as I talk about them, and setting them on the person page keeps everything about that person in one place.
