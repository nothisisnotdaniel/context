# 014 Remove the interactive console

As a maintainer, I want the interactive console removed once the desktop app and CLI cover everything, so that there's one user interface to keep working instead of two.

## What

Once the desktop app covers notes, todos, the calendar link, import and export, and tags and links, and the scriptable CLI exists, delete the interactive session (layout, input box, line editor, popup, sidebar, mouse handling) and its tests, drop Spectre.Console if nothing else needs it, and update CLAUDE.md and the README. Builds on all desktop lines and the scriptable CLI.

## Why

Every feature currently has to be built twice. When the desktop app and the scriptable CLI cover the same ground, the console session is only a cost, and Spectre.Console can probably go with it.
