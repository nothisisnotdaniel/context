# 485 Shared AI layer with preview, budget and logs

As someone who wants AI help but not surprises, I want every AI request shown to me before it is sent and its cost capped and logged, so that I stay in control of what leaves my machine and what it costs.

Category: feature

## What

One Core layer every AI feature goes through: it shows the exact text to be sent and the model and asks for approval each time (no sensitive note ever included); a monthly budget in Settings that refuses requests once reached; an append-only cost log (date, feature, model, tokens, cost) that Context never rewrites; an AI error log with fixed texts. The API key lives in the OS key store, never in the data folder. No feature uses it yet beyond a test call.

## Why

assistant builds AI with preview, approval, budget, an immutable cost log and an error log. Building that once, before the first AI feature (#59 Ask Context), keeps every later feature safe by default.

## Notes

Idea from Creacility/assistant PLAN.md (compared on 2026-11-22). Comes before #59 Ask Context and #95 Local AI.
