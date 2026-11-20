# 094 Browser extension

As the user, I want to decide later on a browser extension that sends pages to the Context inbox and shows the Times status, so that capturing from the web is one click.

## What

A new extension (its own project) for Chrome and Edge that sends the current page or a selection to the Context inbox with its title and link, and shows the Times status (checked in, time left, open checkpoint) in the toolbar. Talks to the scriptable CLI or a small local endpoint; nothing goes over the network. Decide after the scriptable CLI.

## Why

It would be a new project and depends on the scriptable CLI or a local endpoint, so it waits for that.
