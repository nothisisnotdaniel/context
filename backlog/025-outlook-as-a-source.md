# 025 Outlook as a source

As a Context user on Windows, I want Context to read my calendar and recent mail from classic Outlook, so that work appointments and mail show up without an extra calendar link.

## What

Read today's calendar entries and recent mail from the running classic Outlook through its COM interface, using the profile that is already signed in (no app registration, no tokens stored). Off by default; only offered on Windows when classic Outlook is installed, and a clear hint otherwise (new Outlook has no COM). Use Outlook's own expansion of recurring meetings. Appointments go into `/current` next to the `.ics` sources. Expect a possible Outlook security prompt. A new interop package needs a reason in the spec (the approver will escalate it). Microsoft Graph and Copilot are out of scope.

## Why

Many work setups use classic Outlook, and its COM interface works with the profile that's already signed in, so there are no tokens to store. It's off by default and Windows-only.
