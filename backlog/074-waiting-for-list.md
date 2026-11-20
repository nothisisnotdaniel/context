# 074 Waiting-for list

As a Context user, I want to track what I'm waiting on from whom, so that I can follow up instead of forgetting delegated work.

## What

A todo written as `waiting: @[Full Name] <text>` (with an optional follow-up date) is something the user waits on from that person. The desktop app shows these in their own sidebar group, the person page lists what is still open with them, and the morning briefing shows the follow-ups due. Inbox triage gets a "delegate" key that turns a line into such a todo. Builds on people pages and inbox triage.

## Why

Delegated tasks vanish from view once handed off. A waiting-for todo per person shows up on their page and in the morning briefing when it's time to follow up.
