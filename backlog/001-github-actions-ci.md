# 001 GitHub Actions CI

As a maintainer, I want Context built and tested on GitHub for every push and pull request, so that the factory's CI wait really checks Context jobs.

## What

Add a workflow in `.github/workflows/` that sets up .NET 10, builds with 0 warnings and runs `dotnet test` on pushes to `develop` and `main` and on pull requests, so the factory's CI wait checks Context jobs instead of skipping them. No Node needed (the editor bundle is committed).

## Why

Today the factory skips CI for Context because there is no workflow, so a merge that breaks the build on a clean machine goes unnoticed. A workflow with 0 warnings and `dotnet test` gives every job an independent check.
