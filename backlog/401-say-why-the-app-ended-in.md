# 401 Say why the app ended in the crash log

As the user, I want an unclean-exit log to say how the app ended, so that a crash report points at its cause.

## What

Today an exit that skips crashLog.End() after app.Run() only gives 'the app was killed or the web view stopped'. Record the way out in .logs/session before the process goes: AppDomain.ProcessExit (Environment.Exit, a normal CLR shutdown), the Windows session ending (logoff, shutdown, restart for updates) and a WebView2 process failure (CoreWebView2.ProcessFailed with its kind and exit code, if Photino exposes the CoreWebView2 object; otherwise say in the spec that it can't). The next start's unclean-exit log names that reason, or says none was recorded (then the process was killed). Keep CrashLog's rules: never throws, no links followed, fixed texts, no personal data.

## Why

Issue #1 could not be diagnosed: one log line can't tell a kill, a logoff and a web view crash apart.

## Notes

Added on 2026-11-20 from Context issue #1.
