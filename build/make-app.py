"""Publish VstManager.Ui and lay it out as a macOS .app bundle.

Runs on Windows — the bundle is just a directory tree with a plist, so it can be assembled
anywhere; only signing and notarisation genuinely require a Mac. What is produced here is
therefore an *unsigned* bundle, which macOS Gatekeeper will quarantine on first launch until it
is signed or the user clears the attribute. See the note at the end of the run.

Usage (from the repo root):
    python build/make-app.py                 # osx-arm64, Release
    python build/make-app.py --rid osx-x64
    python build/make-app.py --no-publish    # relayout an existing publish, skip dotnet
"""

import argparse
import os
import plistlib
import re
import shutil
import stat
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJ = os.path.join(REPO, "src", "VstManager.Ui")
CSPROJ = os.path.join(PROJ, "VstManager.Ui.csproj")
ICNS = os.path.join(PROJ, "Assets", "app-icon.icns")
PLIST_SRC = os.path.join(REPO, "build", "Info.plist")

APP_NAME = "VST Manager.app"
EXE_NAME = "VstManager.Ui"


def project_version():
    """The <Version> from the csproj, so the bundle cannot drift from the assembly."""
    text = open(CSPROJ, encoding="utf-8").read()
    m = re.search(r"<Version>([^<]+)</Version>", text)
    return m.group(1).strip() if m else "1.0.0"


def publish(rid, configuration, out_dir):
    cmd = [
        "dotnet", "publish", CSPROJ,
        "-c", configuration,
        "-r", rid,
        "--self-contained", "true",
        "-o", out_dir,
    ]
    print("$ " + " ".join(cmd))
    subprocess.run(cmd, check=True)


def build_bundle(publish_dir, bundle_dir, version):
    if os.path.exists(bundle_dir):
        shutil.rmtree(bundle_dir)

    macos = os.path.join(bundle_dir, "Contents", "MacOS")
    resources = os.path.join(bundle_dir, "Contents", "Resources")
    os.makedirs(macos)
    os.makedirs(resources)

    # Everything the publish produced goes beside the executable. .NET resolves its runtime
    # relative to the binary, so the whole payload has to live in Contents/MacOS — splitting
    # managed DLLs into Resources/ would break the host.
    for entry in os.listdir(publish_dir):
        src = os.path.join(publish_dir, entry)
        dst = os.path.join(macos, entry)
        if os.path.isdir(src):
            shutil.copytree(src, dst)
        else:
            shutil.copy2(src, dst)

    exe = os.path.join(macos, EXE_NAME)
    if not os.path.exists(exe):
        sys.exit(f"expected executable not found in publish output: {exe}")

    # Mark the entrypoint executable. On Windows this bit is not stored in the filesystem, so a
    # bundle assembled here still needs `chmod +x` on the Mac — reported at the end.
    os.chmod(exe, os.stat(exe).st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    if os.path.exists(ICNS):
        shutil.copy2(ICNS, os.path.join(resources, "app-icon.icns"))
    else:
        print(f"WARNING: {ICNS} missing — run build/make-icns.py first; the bundle will "
              f"show a generic icon.")

    with open(PLIST_SRC, "rb") as fh:
        raw = fh.read().decode("utf-8").replace("__VERSION__", version)

    plist_path = os.path.join(bundle_dir, "Contents", "Info.plist")
    with open(plist_path, "wb") as fh:
        fh.write(raw.encode("utf-8"))

    # Parse it back: a malformed plist makes the bundle silently unlaunchable, and that failure
    # would otherwise only show up on a Mac.
    with open(plist_path, "rb") as fh:
        parsed = plistlib.load(fh)

    for key in ("CFBundleExecutable", "CFBundleIdentifier", "CFBundleShortVersionString"):
        if not parsed.get(key):
            sys.exit(f"Info.plist is missing {key}")

    if parsed["CFBundleExecutable"] != EXE_NAME:
        sys.exit(f"CFBundleExecutable {parsed['CFBundleExecutable']!r} does not match the "
                 f"published binary {EXE_NAME!r}")

    return parsed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rid", default="osx-arm64", choices=["osx-arm64", "osx-x64"])
    ap.add_argument("--configuration", default="Release")
    ap.add_argument("--no-publish", action="store_true")
    args = ap.parse_args()

    version = project_version()
    publish_dir = os.path.join(REPO, "artifacts", "publish", args.rid)
    bundle_dir = os.path.join(REPO, "artifacts", args.rid, APP_NAME)

    if not args.no_publish:
        publish(args.rid, args.configuration, publish_dir)
    elif not os.path.isdir(publish_dir):
        sys.exit(f"--no-publish given but {publish_dir} does not exist")

    parsed = build_bundle(publish_dir, bundle_dir, version)

    print()
    print(f"bundle:     {bundle_dir}")
    print(f"version:    {parsed['CFBundleShortVersionString']}")
    print(f"identifier: {parsed['CFBundleIdentifier']}")
    print(f"executable: Contents/MacOS/{parsed['CFBundleExecutable']}")
    print()
    print("UNVERIFIED: assembled on Windows and never launched on macOS. On the Mac:")
    print(f"  chmod +x '{APP_NAME}/Contents/MacOS/{EXE_NAME}'   # exec bit is not preserved by Windows")
    print(f"  xattr -dr com.apple.quarantine '{APP_NAME}'        # unsigned build")
    print(f"  codesign --deep -s - '{APP_NAME}'                  # ad-hoc signature")


if __name__ == "__main__":
    main()
