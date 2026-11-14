# RCH 0.1.15

Includes all preview, background refresh, streaming catalog, custom icon and settings changes from 0.1.14. Fixes download completion when a background catalog refresh replaces the entry during the transfer: completion updates the current record, preserving the downloaded revision and reporting a mismatch if the cloud has already moved to a newer revision.

Regression checks exercise completion after a catalog replacement and completion after a concurrent remote revision change.
