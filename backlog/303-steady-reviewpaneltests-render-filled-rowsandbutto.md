# 303 Steady ReviewPanelTests.Render_Filled_RowsAndButtons

As a developer on Context, I want ReviewPanelTests.Render_Filled_RowsAndButtons to pass on every CI run, so that develop stays green and real failures stand out.

## What

In the develop CI run after job 221 (https://github.com/nothisisnotdaniel/context/actions/runs/44381212548), ReviewPanelTests.Render_Filled_RowsAndButtons failed at tests/Context.Desktop.Tests/ReviewPanelTests.cs:101 with Assert.Equal() Failure: Expected ["Plan"], Actual [] (the click on the Plan button was not seen; the close click was). It passed 15 of 15 locally when run alone. ReviewPanel inherits StateComponent, which job 221 (merge 682bac7) changed to count state reloads. Find out whether that change can drop or delay the button click under load (for example a reload re-rendering the panel between Find and Click) and fix the cause in the code if so; otherwise make the test deterministic without weakening what it checks. Keep MainTests.WindowClose_ClosedAfterLastRender_IsStillSeen steady.

## Why

develop CI is red after job 221; a flaky or newly broken test blocks the auto run and hides real failures.

## Notes

Requested by the user on 2026-11-18 after the CI failure of job 221.
