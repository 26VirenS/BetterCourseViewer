#!/usr/bin/env python3
"""The Mac builds' incremental compile (1.3.1): a fresh checkout dates every file now, so Xcode would compile all of
them again even with the last build's intermediates restored. `save <dir>` writes, beside the build in <dir>, each
tracked file's hash and the date it had when that build was made; `restore <dir>` gives every file whose content is
the same its old date back — so only what changed (dated now, newer than the build) is compiled again.

  python3 scripts/dev/mtime-cache.py save build      (after a build)
  python3 scripts/dev/mtime-cache.py restore build   (after the checkout and the build's restore, before building)
"""
import hashlib
import json
import os
import subprocess
import sys


def tracked():
    out = subprocess.check_output(['git', 'ls-files', '-z'])
    return [p for p in out.decode('utf-8', 'surrogateescape').split('\0') if p and os.path.isfile(p)]


def digest(path):
    h = hashlib.sha1()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def main():
    if len(sys.argv) != 3 or sys.argv[1] not in ('save', 'restore'):
        sys.exit(__doc__)
    mode, folder = sys.argv[1], sys.argv[2]
    record = os.path.join(folder, '.mtimes-ns.json') # (a record of float seconds is not used: it lost the nanoseconds)
    if mode == 'save':
        os.makedirs(folder, exist_ok=True)
        # (to the nanosecond: the Swift driver compares a file's date exactly with the one its last build recorded)
        data = {p: [digest(p), os.stat(p).st_mtime_ns] for p in tracked()}
        with open(record, 'w') as f:
            json.dump(data, f)
        print(f'mtimes: {len(data)} files recorded')
        return
    try:
        with open(record) as f:
            data = json.load(f)
    except (OSError, ValueError):
        print('mtimes: no record (a clean build)')
        return
    same = changed = 0
    for p in tracked():
        kept = data.get(p)
        if kept and kept[0] == digest(p):
            os.utime(p, ns=(int(kept[1]), int(kept[1])))
            same += 1
        else:
            changed += 1
    print(f'mtimes: {same} files as before, {changed} new or changed')


if __name__ == '__main__':
    main()
