# 106 Claude Code MCP server

As a developer who uses Claude Code, I want Claude to read my notes, todos, calendar and hours through a local MCP server, so that I can ask things like 'what did I decide about AB#1234?' without copying text over.

## What

A `context mcp` command that runs a local MCP server over stdio (using the official C# MCP SDK) with read-only tools: search notes, read a note, list todos, today's calendar, the work item dossier and hours from Times. No network listener, no write tools in this item, and `#private` notes are never returned. The README shows how to add it to Claude Code. Full path, high risk (exposes local data to an AI client).

## Why

Developers already using Claude Code are the easiest buyers to reach, and giving Claude the context of their own workday is something no notes or time tool offers. Write tools (booking time, adding todos) can follow once reading is safe.
