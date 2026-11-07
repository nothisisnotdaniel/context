# 056 One project list

As a Context and Times user, I want Context to offer Times' work items as tags, so that both apps use the same project numbers without keeping two lists.

## What

Context reads Times' `projects.json` (read-only, regular file, capped size, Times' data folder rules) and offers its work items as `#<number>` tags with their titles, for example in tag completion and on tag pages. Projects are still added in Times (or with `times addproject` once Times has one-shot commands). If Times isn't installed, nothing changes.

## Why

Keeping work items in Times and typing them again in Context invites typos and drift. Reading Times' file read-only keeps Times as the one place to add them.
