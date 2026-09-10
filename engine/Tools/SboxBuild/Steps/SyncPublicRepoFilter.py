#!/usr/bin/env python3

"""s&box public repository filtering helpers."""

import argparse
import json
import posixpath
import subprocess
import sys
from pathlib import PurePosixPath
from typing import Dict, Iterable, List, Optional, Set
import git_filter_repo as fr

_LFS_POINTER_PREFIX = b"version https://git-lfs.github.com/spec/v1"

SHADER_POLICY_BOUNDARY = "2f5eac37b6314c3ac604fcf867ea42c60da91ebb"

# Frozen from SyncPublicRepo.cs at SHADER_POLICY_BOUNDARY^, not current config.
# Future visibility expansions also need forward-only boundaries; changing the
# timeless filename policy alone rewrites already-published history.
LEGACY_SHADER_ALLOWLIST = (
    "game/core/shaders/**/vr_*",
    "game/core/shaders/**/*.hlsl",
    "game/core/shaders/**/*.shader_c",
    "game/core/shaders/common.fxc",
    "game/core/shaders/common_samplers.fxc",
    "game/core/shaders/descriptor_set_support.fxc",
    "game/core/shaders/system.fxc",
    "game/core/shaders/tiled_culling.hlsl",
    "game/core/shaders/skinning_cs.shader",
    "game/core/shaders/yuv_resolve.shader",
    "game/core/shaders/sbox_pixel.fxc",
    "game/core/shaders/sbox_shared.fxc",
    "game/core/shaders/sbox_vertex.fxc",
)


class FilenameFilter:
    """Applies include/exclude rules and renames."""

    def __init__(self, config: Dict[str, object]) -> None:
        self._include_globs = tuple(_normalise_glob(p) for p in config.get("include_globs", []) or [])
        self._exclude_globs = tuple(_normalise_glob(p) for p in config.get("exclude_globs", []) or [])

        renames = config.get("path_renames", {}) or {}
        self._rename_targets: Dict[str, str] = {
            _normalise_path(src): str(dest)
            for src, dest in renames.items()
        }

    def __call__(self, filename: bytes) -> Optional[bytes]:
        path_text = filename.decode("utf-8", "ignore")
        normalised = _normalise_path(path_text)
        path = PurePosixPath(normalised)

        allowed = _matches_any_glob(path, self._include_globs)

        if allowed and _matches_any_glob(path, self._exclude_globs):
            allowed = False

        if not allowed:
            return None

        rename_target = self._rename_targets.get(normalised)
        if rename_target:
            return rename_target.encode("utf-8")

        return filename


class ShaderPolicyFilter:
    """Gate shader visibility by original ancestry and import the boundary tree."""

    def __init__(self, filename_filter, boundary=SHADER_POLICY_BOUNDARY) -> None:
        self._boundary = boundary.encode("ascii")
        history = subprocess.check_output(
            ["git", "rev-list", "--reverse", "--topo-order", "--parents", "--all"]
        ).splitlines()
        if not any(line.split()[0] == self._boundary for line in history):
            raise RuntimeError(
                f"Shader policy boundary {boundary} is not reachable/included in the filter history; "
                "restore history containing the boundary before filtering."
            )

        self._modern_commits = set()
        for line in history:
            commit, *parents = line.split()
            # The published history is linear. Cross-policy merges require tree
            # reconciliation, not just filtering deltas; reject merges up front.
            if len(parents) > 1:
                raise RuntimeError(
                    f"Shader policy filtering does not support merge commits ({commit.decode()}); "
                    "policy-crossing merges require explicit tree reconciliation."
                )
            if commit == self._boundary or any(p in self._modern_commits for p in parents):
                self._modern_commits.add(commit)

        self._imports = []
        self._blob_data = {}
        # Read the complete B tree and its blobs before fast-export/fast-import
        # start. Unchanged files will not appear in B's delta, and HEAD is wrong.
        tree = subprocess.check_output(["git", "ls-tree", "-rz", boundary])
        for entry in tree.split(b"\0"):
            if not entry:
                continue
            info, filename = entry.split(b"\t", 1)
            mode, kind, oid = info.split()
            if not self._new_shader(filename):
                continue
            target = filename_filter(filename)
            if target is None:
                continue
            if kind != b"blob":
                raise RuntimeError(f"Unsupported shader tree entry at boundary: {filename!r} ({kind!r})")
            self._imports.append((target, mode, oid))
            if oid not in self._blob_data:
                self._blob_data[oid] = subprocess.check_output(["git", "cat-file", "blob", oid.decode()])

    @staticmethod
    def _new_shader(filename: bytes) -> bool:
        path = _normalise_path(filename.decode("utf-8", "ignore"))
        return path.startswith("game/core/shaders/") and not _matches_any_glob(
            PurePosixPath(path), LEGACY_SHADER_ALLOWLIST
        )

    def filter_commit(self, commit, repo_filter) -> None:
        # Filename callbacks are cached, so they must remain epoch-independent.
        if commit.original_id not in self._modern_commits:
            commit.file_changes = [c for c in commit.file_changes if not self._new_shader(c.filename)]
        elif commit.original_id == self._boundary:
            marks = {}
            for oid, data in self._blob_data.items():
                blob = fr.Blob(data)
                # Default insertion runs the blob callback, including LFS and
                # symlink detection. Raw OIDs would bypass mark-based stripping.
                repo_filter.insert(blob)
                marks[oid] = blob.id
            changes = {c.filename: c for c in commit.file_changes}
            for filename, mode, oid in self._imports:
                changes[filename] = fr.FileChange(b"M", filename, marks[oid], mode)
            commit.file_changes = [c for _, c in sorted(changes.items())]


class LfsPointerFilter:
    """Strips LFS pointer blobs and dangling symlinks from commits."""

    def __init__(self) -> None:
        self._lfs_blob_ids: Set[int] = set()
        self._symlink_targets: Dict[int, str] = {}
        self._stripped_paths: Set[str] = set()

    def blob_callback(self, blob, _metadata) -> None:
        if blob.data.startswith(_LFS_POINTER_PREFIX):
            self._lfs_blob_ids.add(blob.id)
        elif len(blob.data) < 512:
            try:
                self._symlink_targets[blob.id] = blob.data.decode("utf-8").rstrip("\n")
            except UnicodeDecodeError:
                pass

    def strip_lfs_from_commit(self, commit) -> None:
        original = commit.file_changes

        stripped_this_commit: Set[str] = set()
        after_lfs = []
        for change in original:
            if change.blob_id in self._lfs_blob_ids:
                stripped_this_commit.add(change.filename.decode("utf-8", "replace"))
                continue
            after_lfs.append(change)

        filtered = []
        for change in after_lfs:
            if change.mode == b"120000" and change.blob_id in self._symlink_targets:
                target = self._symlink_targets[change.blob_id]
                symlink_dir = PurePosixPath(change.filename.decode("utf-8", "replace")).parent
                resolved = posixpath.normpath(str(symlink_dir / target))
                if resolved in stripped_this_commit:
                    stripped_this_commit.add(change.filename.decode("utf-8", "replace"))
                    continue
            filtered.append(change)

        self._stripped_paths.update(stripped_this_commit)
        commit.file_changes = filtered

    def log_summary(self) -> None:
        print(f"[LfsPointerFilter] Detected {len(self._lfs_blob_ids)} LFS pointer blob(s)")
        print(f"[LfsPointerFilter] Stripped {len(self._stripped_paths)} unique path(s) from history")
        if self._stripped_paths:
            for path in sorted(self._stripped_paths):
                print(f"  - {path}")


class BaselineCommitCallback:
    """Rewrites the root commit metadata."""

    _base_message = (
        "Open source release\n\n"
        "This commit imports the C# engine code and game files, excluding C++ source code."
    )

    def __call__(self, commit, metadata) -> None:
        if commit.parents:
            return

        commit.message = self._base_message.encode("utf-8")
        commit.message += b"\n\n[Source-Commit: " + commit.original_id + b"]\n"
        commit.author_name = b"s&box team"
        commit.author_email = b"sboxbot@facepunch.com"
        commit.committer_name = b"s&box team"
        commit.committer_email = b"sboxbot@facepunch.com"


def _normalise_path(value: str) -> str:
    return value.replace("\\", "/").lower()


def _normalise_glob(pattern: str) -> str:
    return _normalise_path(pattern or "")


def _matches_any_glob(path: PurePosixPath, patterns: Iterable[str]) -> bool:
    for glob in patterns:
        if path.full_match(glob):
            return True
    return False


def filter_repository(config, boundary=SHADER_POLICY_BOUNDARY) -> None:
    filename_filter = FilenameFilter(config)
    shader_filter = ShaderPolicyFilter(filename_filter, boundary)
    baseline_callback = BaselineCommitCallback()
    lfs_filter = LfsPointerFilter()

    def commit_callback(commit, metadata):
        shader_filter.filter_commit(commit, repo_filter)
        lfs_filter.strip_lfs_from_commit(commit)
        baseline_callback(commit, metadata)

    options = fr.FilteringOptions.parse_args([], error_on_empty=False)
    options.force = True

    repo_filter = fr.RepoFilter(
        options,
        blob_callback=lfs_filter.blob_callback,
        filename_callback=filename_filter,
        commit_callback=commit_callback,
    )

    repo_filter.run()
    lfs_filter.log_summary()


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    args = parser.parse_args(argv)

    with open(args.config, "r", encoding="utf-8") as fp:
        config = json.load(fp)

    try:
        filter_repository(config)
    except RuntimeError as ex:
        parser.exit(1, f"Shader policy filter failed: {ex}\n")
    return 0

if __name__ == "__main__":
    sys.exit(main())
