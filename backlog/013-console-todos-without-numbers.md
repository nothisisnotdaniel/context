# 013 Console todos without numbers

As a Context user, I want a clear way to pick a todo in the console now that todos have hidden ids, so that `/done` keeps working reliably.

## What

Decide how the console addresses a todo now that todos have hidden ids (the desktop shows checkbox lines only); today the sidebar numbers them and `/done <n>` picks by number, then removes by id. Update ViewParityTests to match.

## Why

The console picks todos by number but removes them by id, while the desktop shows checkbox lines only. The two need one consistent rule, and the parity tests have to follow it.
