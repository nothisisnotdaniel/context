# 101 Work item references

As a developer on Azure DevOps, I want Context to recognise work item references like `AB#1234` in my notes and todos, so that everything about one work item can be found together.

## What

Recognise `AB#<number>` (the Azure Boards convention) in notes and todos, index the references in `context.db` next to the tags, and add `context item <number>` that lists the notes and todos that mention it (with `--json`). Plain `#1234` stays untouched, because tags must start with a letter. Full path.

## Why

Tags group by topic, but a developer's day is organised by work items. One reference format that Times, commits and notes share is the glue for the whole suite.
