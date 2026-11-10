---
name: sbox-publishing
description: Check that an s&box project's publishing rules include all required content and flag unexpectedly large assets.
---

# Check publishing content

Make sure everything the game needs is included. Inspect scenes, code paths and dynamic resource
loads; don't assume an unreferenced asset is unused. Flag unusually large assets and their
dependencies for review, even when referenced, rather than removing needed content.

Use `sbox-cli --skill sbox-cli` to connect to the project's existing editor and discover
`publishing_preview`. Pass supplied `resources` and `publishUsedAssetsOnly` explicitly: unsaved
UI edits aren't read automatically. Read the baseline and proposed preview reports to compare
added/removed files and size changes.

Resource Files uses ordered wildcards: `!` excludes and the last match wins. Configured scenes,
Resources folders and required dependencies still ship. Exact code paths are recommendations,
not automatic includes. Preserve intentional exclusions and flag conflicts with required content.

Show proposed changes, uncertain cases and large costs. Wait for confirmation and ask the user
to close Project Settings before saving; its stale values could overwrite your changes. Keep
the editor open for previews. Then update only the agreed `Resources` and `PublishUsedAssetsOnly`
fields in the `.sbproj`, preserving other settings. Re-read the file before editing and verify
the saved settings with another preview. Don't publish or delete assets.
