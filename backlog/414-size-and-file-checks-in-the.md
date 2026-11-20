# 414 Size and file checks in the dependency list test

As the Context maintainer, I want the dependency list test to refuse oversized or non-regular files, so that a stray file in the repo cannot stall the test run.

Category: security

## What

In tests/Context.Tests/DependencyList.cs, the repo files (csproj, package.json, DEPENDENCIES.md, THIRD-PARTY-LICENSES.txt) are read without a size limit or a regular-file check. Add both, failing the test with a clear message.

## Why

Raised as a nit by the security review of job 339: these are committed files read only by dotnet test, so it is not a trust boundary, but a check costs little.

## Notes

Proposed by job 339-dependency-licence-list (`factory/jobs/339-dependency-licence-list/`).
