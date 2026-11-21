# 450 Remove unreachable glossary TooLong message

As a maintainer, I want the glossary panel to show only messages a user can reach, so that the code says what really happens.

Category: ux

## What

The glossary filter field has a maxlength, so the panel's TooLong message can't be reached. Remove it or align the field limit with GlossaryView's 200-char rule.

## Why

Job 380's ux review: the TooLong message can't be reached because of maxlength.

## Notes

Proposed by job 380-glossary-terms (`factory/jobs/380-glossary-terms/`).
