# 099 Refuse odd files on import

As a Context user, I want a decision on whether `/import` refuses zero-length and non-regular files, so that import handles odd files on purpose.

## What

Refusing zero-length or non-regular files on import changes /import behaviour

## Why

Refusing them changes how `/import` behaves today, so job 123 left it for my approval.

## Notes

Proposed by job 123-scriptable-cli (`factory/jobs/123-scriptable-cli/`).
