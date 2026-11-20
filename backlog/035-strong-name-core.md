# 035 Strong-name Core

As a maintainer, I want Core's internals protected properly before it ships on its own, so that another assembly can't use them just by having the same name.

## What

`InternalsVisibleTo` in `Context.Core` matches by assembly name only; strong-name the assemblies or give Core a deliberate public API before it ships on its own.

## Why

`InternalsVisibleTo` matches by assembly name only. Strong-naming, or a deliberate public API, is needed before Core is shared.
