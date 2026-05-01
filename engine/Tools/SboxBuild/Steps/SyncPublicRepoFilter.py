#!/usr/bin/env python3

"""s&box public repository filtering helpers."""

import argparse
import json
import posixpath
import sys
from pathlib import PurePosixPath
from typing import Dict, Iterable, List, Optional, Set
import git_filter_repo as fr

_LFS_POINTER_PREFIX = b"version https://git-lfs.github.com/spec/v1"


class FilenameFilter:
    """Applies include/exclude rules and renames."""

    def __init__(self, config: Dict[str, object]) -> None:
        self._include_globs = tuple(_normalise_glob(p) for p in config.get("include_globs", []) or [])
        self._exclude_globs = tuple(_normalise_glob(p) for p in config.get("exclude_globs", []) or [])
        self._whitelisted_shaders = tuple(_normalise_glob(p) for p in config.get("whitelisted_shaders", []) or [])

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

        if not allowed and _matches_any_glob(path, self._whitelisted_shaders):
            allowed = True

        if not allowed:
            return None

        rename_target = self._rename_targets.get(normalised)
        if rename_target:
            return rename_target.encode("utf-8")

        return filename


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


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    args = parser.parse_args(argv)

    with open(args.config, "r", encoding="utf-8") as fp:
        config = json.load(fp)

    filename_filter = FilenameFilter(config)
    baseline_callback = BaselineCommitCallback()
    lfs_filter = LfsPointerFilter()

    def commit_callback(commit, metadata):
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
    return 0

if __name__ == "__main__":
    sys.exit(main())
