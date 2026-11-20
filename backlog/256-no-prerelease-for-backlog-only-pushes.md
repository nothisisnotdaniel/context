# 256 No prerelease for backlog-only pushes

As the Context maintainer, I want pushes that only change the backlog or its user stories to build no prerelease, so that the releases list holds real builds only and no runner time is spent on text edits.

## What

In .github/workflows/release.yml, the develop push trigger already skips a push that changes only the root backlog.md (paths-ignore). Extend it so a push whose changes are all in backlog.md and/or backlog/** (the numbered user stories) makes no prerelease either. Any push that also touches anything else still builds as today; version tags and the main/PR behaviour stay unchanged. Keep ci.yml as it is: it must keep running on every pushed commit, because the factory's CI wait looks up runs by commit. Update the comment at the top of release.yml (and the release notes in CLAUDE.md, if they describe this) to match.

## Why

Every backlog addition or story edit (several a day) currently makes a develop prerelease because only backlog.md is ignored, not the story files under backlog/.

## Notes

Requested by the user on 2026-11-17. Check the result with a backlog-only push after merge: no Release run, CI run still present.
