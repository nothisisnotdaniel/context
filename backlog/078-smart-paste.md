# 078 Smart paste

As a Context user, I want pasted links and spreadsheet cells turned into proper Markdown, so that pasted content looks right without manual cleanup.

## What

Pasting into a note in the desktop app turns a lone web link into `[page title](url)` (title fetched only if a setting allows network access, with a short timeout, otherwise the link stays as it is) and tab-separated cells (from Excel or Teams) into a Markdown table. Ctrl/Cmd+Shift+V pastes plain text as it is. Builds on desktop editing.

## Why

Pasting from the browser, Excel or Teams produces bare URLs and tab-separated text. Fetching titles only when a setting allows network access keeps it private.
