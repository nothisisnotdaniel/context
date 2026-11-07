# 038 Core dependency test tidy-ups

As a maintainer, I want the Core dependency test to explain its rule and skip build folders at any depth, so that a failure is clear and the scan doesn't trip over build output.

## What

`CoreDependencyTests` failure messages also state the rule (Core stays free of Spectre so other front ends, like the desktop app, can use it), and its source scan skips `bin/` and `obj/` at any depth, not only at the top of the project folder.

## Why

The test keeps Core free of Spectre so other front ends can use it, but its failure message doesn't say so.
