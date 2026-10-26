# Source 1 mount

The engine's `gmod` mount reads supported models, materials, textures and sounds
from GMA/VPK archives and loose files inside the Garry's Mod installation.

## Registration and archives

The installation is inventoried in one filesystem pass, including nested addon,
gamemode content, download and cache folders. Directory links/reparse points are
skipped to avoid cycles or leaving the installation. Unreadable directories are
skipped with a warning while other directories continue mounting. Files directly
in the installation root are included; top-level `materials`, `models`, `sound` and
`scripts` directories retain their canonical prefixes.

Archives precede loose files. Archives inside `garrysmod` take priority over
other installation folders, with GMAs before VPKs in each priority group and
case-insensitive path sorting for stable first-match resolution. Loose content
keeps its indexed paths without unprefixed addon or gamemode aliases.
Shadowed loose files are logged at trace verbosity; the first indexed source
continues to win.
Model registration uses only companion filenames; models without a VVD/DX90 VTX
pair or a PHY companion remain indexed for dependencies but are not advertised.
Texture headers register the supported individual frames and animation arrays.

GMA versions 1-3 share the VPK reader's leased random-access stream implementation.
Indexing reads directories, not whole payloads; assets load lazily without
extracting archives. Invalid paths, duplicate GMA entries and out-of-bounds data
are rejected. Invalid or unreadable archives are logged as warnings without
marking otherwise valid mounted content as erroneous. Split VPK chunks with a
matching directory archive are skipped, and alternate names for the same VPK
directory are deduplicated.

Only supported model, material, texture and sound resources are registered;
Lua is not executed and BSP/map import is not included. Local Workshop GMA files
inside the installation are supported, but external Steam Workshop folders and
automatic Workshop downloads are not discovered. Remount after downloading or
changing content.

## Material dependency resolution

VMT parameters target Windows PC DX90/PS2.0b. Shader fallback blocks such as
`>=dx90_20b` and the `$DX90_20B` condition are supported. Both combine soldier
skins keep their base texture and self-illumination mask from those blocks.

Loose models resolve their MDL material paths within their enclosing content root
before falling back to ordinary mounted content. VMT patch includes, relative
KeyValues directives, textures and HDR environment maps keep that same root.
For example, `addons/example/models/` resolves against
`addons/example/materials/` without adding global aliases. Full indexed resource
paths keep separate addons' materials and texture caches distinct. VMTs retain
their actual indexed entry paths rather than receiving an extra `materials/`
prefix. Reimport existing models to replace old error-material bindings.

There is no arbitrary basename matching or cross-installation dependency
fallback. Genuinely missing assets remain reported rather than receiving
unrelated replacement materials.
