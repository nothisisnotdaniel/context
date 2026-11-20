# 036 CLAUDE.md tidy-ups

As a maintainer, I want small wording and wrapping fixes in Context's CLAUDE.md, so that the conventions every agent reads are clean and accurate.

## What

Merge the two adjacent parentheticals in the tests bullet, say that test files use `namespace Context.Tests;` (file-scoped), and rewrap the sentence under "Data step in Core" (job 042) and the `/current` bullet (job 046, about 150 characters) to the surrounding line width.

## Why

A few bullets are clumsy or too long after jobs 042 and 046, and the file-scoped test namespace isn't stated.
