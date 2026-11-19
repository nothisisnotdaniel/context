# 357 Similar notes: limit pairs when many notes share a title

As a desktop user with many notes of the same title, I want /similar to stay fast, so that the panel opens quickly even with dozens of "Meeting" notes.

## What

SimilarNotes.TitlePairs/Pairs (src/Context.Core/SimilarNotes.cs) build every pair inside a title key, which grows with n². Cap or group pairs that share a key (for example list a group once, or limit pairs per key) and add a test with many same-title notes.

## Why

Security review of job 268: the cost grows quadratically with notes sharing a title; it is the user's own data, so it is a speed issue, not an attack.

## Notes

Proposed by job 268-similar-notes (review-security 4).

Proposed by job 268-similar-notes (`factory/jobs/268-similar-notes/`).
