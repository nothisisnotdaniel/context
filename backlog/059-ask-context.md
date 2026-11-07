# 059 Ask Context

As a Context user, I want to ask a question and get an answer from my notes with links to the sources, so that I can find knowledge without remembering where I wrote it.

## What

Ask a question in plain language in the desktop app; Context picks the most relevant passages from the notes (full-text search first, embeddings later) and an AI model writes an answer that cites the notes it used, each a link. Off by default; the model is set in the settings, and before anything leaves the machine the app shows which passages would be sent and asks each time. Builds on full-text search.

## Why

Search finds words, but a question often needs an answer pieced together from several notes. Sending passages to a model is a privacy decision, so it's off by default and asks each time, showing what would be sent.
