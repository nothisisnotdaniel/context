# 072 Command bar

As a Context and Times user, I want a Spotlight-style command bar for both apps, so that I can book time or capture a todo from anywhere with a few keystrokes.

## What

A global hotkey opens a small one-line window from the Context desktop app (Spotlight-style), where `cp 1234 review`, `a`, `l` go to Times through `times <command>` and `todo tomorrow …`, `note …`, `capture …` go to Context, with completion of commands and work items. Shows the command's one-line result, then closes. Builds on the scriptable CLI and Times' one-shot commands.

## Why

Switching to a window for a one-line action breaks focus. A global hotkey that routes to `times` or Context and closes again keeps me in the flow.
