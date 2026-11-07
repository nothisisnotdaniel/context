# 051 People pages

As a Context user, I want `@[Full Name]` to link to a page per person that lists every note and appointment mentioning them, so that I can see everything about someone in one place.

## What

Desktop app, plus a CLI command where it fits. `@[Anna Schmidt]` in a note (full names; `@[` at line start or after whitespace, up to the first `]` on the line, outside code like tags; trimmed, whitespace runs become one space, matched ignoring case) links to the person note `people/<Full Name>.md`, created on first use. A person page lists every note and appointment that mentions the person; meeting prep uses it for attendees. Stored in the index like tags (a parser version bump).

## Why

Before talking to someone I want our history at hand, and people are the most natural way to connect work notes. Many later features (waiting-for, 1:1 topics) build on this.
