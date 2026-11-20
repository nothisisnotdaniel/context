# 377 Dependency list with licence links

As the Context maintainer, I want one Markdown file in the repository that lists every dependency with a link to its licence, so that I can see at a glance what Context ships and under which terms.

## What

Add a Markdown file (for example DEPENDENCIES.md at the repo root) that lists every dependency Context uses: NuGet packages (direct and those shipped in the published app), npm packages bundled into the editor (such as lowlight, highlight.js, mermaid), and any vendored assets. For each, give name, version, licence name and a link to its licence text. Mark which are runtime and which are build or test only. Keep it in step with THIRD-PARTY-LICENSES.txt, and add a test or script check that fails when a package in a .csproj or package.json is missing from the list. Link the file from the README.

## Why

The user asked for a list of all dependencies and links to their licences in the repository.

## Notes

Requested by the user on 2026-11-19.
