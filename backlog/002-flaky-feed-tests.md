# 002 Flaky feed tests

As a maintainer, I want the flaky calendar feed tests fixed, so that a red test run means something is really broken.

## What

`CalendarFeedTests.Fetch_Failure_KeepsOldCache_Stale`, `CurrentCommandsTests.Current_Feed_Failed_RedLine_NoSecret` ("over 5 MB (stream)") and `CalendarFeedTests.Fetch_ExactlyFiveMb_Accepted` sometimes fail under a full parallel or cold `dotnet test` run and pass on rerun (seen in 123-scriptable-cli; the feed tests stream 5 MB under a 100 ms test timeout). Find the shared state or timing cause and make them reliable, without weakening the 5 MB cap check.

## Why

Three feed tests sometimes fail under a full or cold run and pass on rerun, because they stream 5 MB under a 100 ms timeout. Flaky tests train everyone to ignore failures and slow down jobs that rerun them.
