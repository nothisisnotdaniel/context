# 525 Make timing and Jint bundle tests reliable under load

Category: test

## What

Make the Jint *BundleTests classes (and the tests that build an EditorBundle), the CalendarStoreTests timing test and the hostile-input regex time-out tests pass when the machine is busy, for example with a load average of 20 while other factory jobs run. Give them limits that scale, or run them in a non-parallel collection, without making them weaker checks.

## Why

During 455 the full dotnet test run failed about 830 tests under load. They all passed when run alone, so builders and approvers had to rerun classes by hand to tell real failures from noise.
