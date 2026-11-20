# 399 Payment provider spike

As the developer of the suite, I want to know which merchant-of-record service should sell Context, so that I can take payments and handle VAT without Lemon Squeezy, which stops processing payments on 31 March 2027.

Category: feature

## What

Lemon Squeezy closed new sign-ups on 16 Nov 2026 and stops processing on 31 Mar 2027 (https://www.lemonsqueezy.com/blog/saying-goodbye-to-lemon-squeezy); its successor is Stripe Managed Payments, which has no built-in digital fulfilment or licence keys. Compare Polar.sh, Stripe Managed Payments, Paddle and FastSpring for selling a cross-platform desktop app (macOS, Windows) from the user's country: merchant of record and VAT/sales tax, fees, payout, supported countries, licence key issuing and validation (online and offline), downloads and updates, refunds, subscriptions vs one-time purchase, API and webhook support, and lock-in or export. Read each provider's current docs and terms; no account is created and no code is written. Write findings with a recommendation and one suggested backlog item for the integration.

## Why

Without a merchant of record the app cannot be sold across countries without registering for VAT everywhere, and the provider choice decides how licence keys work in the app, so it must be settled before any licensing code is built.

## Notes

Added on 2026-11-19 after the user asked what to use instead of Lemon Squeezy.
Times#39 now issues signed offline keys by hand; the recommendation should say how the provider fits next to them (its own keys as a second kind, or a webhook that issues ours) and what that needs.
