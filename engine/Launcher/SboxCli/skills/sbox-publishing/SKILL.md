---
name: sbox-publishing
description: Check that an s&box project's publishing rules include all required content and flag unexpectedly large assets.
---

# Check publishing content

Make sure everything the game needs is included. Inspect scenes, code paths and dynamic resource
loads; don't assume an unreferenced asset is unused. Flag unusually large assets and their
dependencies for review, even when referenced, rather than removing needed content.

Use `sbox-cli --skill sbox-cli` to connect to the project's existing editor and discover
`publishing_preview`. Preview the current rules (use the supplied UI snapshot when present),
read its JSON report, then preview your proposed rules to check what's added or removed.

Resource Files uses ordered wildcards: `!` excludes and the last match wins. Configured scenes,
Resources folders and required dependencies still ship. Exact code paths are recommendations,
not automatic includes. Preserve intentional exclusions and flag conflicts with required content.

Return the proposed rules and briefly explain missing content, uncertain cases and large costs.
For the Copy agent prompt workflow, leave settings unsaved so the user can paste the rules into
the open UI. Don't publish or delete assets.
