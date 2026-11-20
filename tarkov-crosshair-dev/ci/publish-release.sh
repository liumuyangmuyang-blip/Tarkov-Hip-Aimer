#!/usr/bin/env bash
# Publish a release on the PUBLIC repo so installed apps can update themselves (rules: publish-release.md).
#   bash publish-release.sh v5.1 /home/dev/TarkovHipAimer-v5.1.zip [notes.md]   check, then publish
#   bash publish-release.sh --dry-run v5.1 <zip> [notes.md]                              check only, publish nothing
# notes.md (optional) = "what changed" text; the required "sha256: <64 hex>" line is added at the end automatically.
# Needs: ~/.local/bin/gh, signed in once by Leo with `gh auth login` (GITHUB-SETUP-中文.md). No token is read or stored here.
# The app only offers a release that: has a tag newer than the running one and equal to Program.Version in the build,
# is not a draft / pre-release, has a .zip asset whose name contains TarkovHipAimer with exactly one ...TarkovHipAimer.exe
# inside, and has the sha256 line of that exact zip in the notes.
set -euo pipefail
REPO=liumuyangmuyang-blip/Tarkov-Hip-Aimer
GH=${GH:-$HOME/.local/bin/gh}
SRC=/home/dev/tarkov-crosshair-v4/src/Program.cs
DRY=0; ARGS=()
for a in "$@"; do case "$a" in --dry-run) DRY=1;; -h|--help) sed -n 2,9p "$0"; exit 0;; *) ARGS+=("$a");; esac; done
[ ${#ARGS[@]} -ge 2 ] || { echo "usage: $0 [--dry-run] <tag e.g. v5.1> <release zip> [notes.md]" >&2; exit 2; }
TAG=${ARGS[0]}; ZIP=${ARGS[1]}; NOTES=${ARGS[2]:-}

fail() { echo "STOP: $*" >&2; exit 3; }
[[ "$TAG" =~ ^v[0-9]+\.[0-9]+(\.[0-9]+)?$ ]] || fail "tag must look like v5.1 or v5.1.1 (got $TAG)"
[ -f "$ZIP" ] || fail "zip not found: $ZIP"
case "$(basename "$ZIP")" in *TarkovHipAimer*.zip) ;; *) fail "zip name must contain TarkovHipAimer and end in .zip";; esac
[ -z "$NOTES" ] || [ -f "$NOTES" ] || fail "notes file not found: $NOTES"

# tag must equal Program.Version of the source the zip was built from
SRCVER=$(grep -oE 'const string Version = "v[^"]+"' "$SRC" | grep -oE 'v[^"]+' || true)
[ "$SRCVER" = "$TAG" ] || fail "tag $TAG does not match Program.Version in $SRC ($SRCVER)"

# exactly one ...TarkovHipAimer.exe inside the zip
EXES=$(unzip -Z1 "$ZIP" | grep -cE 'TarkovHipAimer\.exe$' || true)
[ "$EXES" = 1 ] || fail "zip must contain exactly one ...TarkovHipAimer.exe (found $EXES)"

SHA=$(sha256sum "$ZIP" | cut -d' ' -f1)
[[ "$SHA" =~ ^[0-9a-f]{64}$ ]] || fail "could not hash the zip"
BODY=$(mktemp); trap 'rm -f "$BODY"' EXIT
{ if [ -n "$NOTES" ]; then cat "$NOTES"; echo; else echo "Tarkov Hip Aimer $TAG"; echo; fi; echo "sha256: $SHA"; } > "$BODY"
[ "$(grep -cE '^sha256: [0-9a-f]{64}$' "$BODY")" = 1 ] || fail "notes must end up with exactly one sha256 line (remove any old one from $NOTES)"

echo "Repo:  $REPO (must stay Public)"
echo "Tag:   $TAG    Zip: $ZIP ($(du -h "$ZIP" | cut -f1))"
echo "Notes:"; sed 's/^/  | /' "$BODY"
if [ "$DRY" = 1 ]; then echo "dry run: nothing published"; exit 0; fi

[ -x "$GH" ] || fail "gh not found at $GH (see GITHUB-SETUP-中文.md)"
"$GH" auth status >/dev/null 2>&1 || fail "gh is not signed in; Leo runs: $GH auth login"
[ "$("$GH" repo view "$REPO" --json visibility -q .visibility)" = PUBLIC ] || fail "$REPO is not Public; apps would see no update"
if "$GH" release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then fail "release $TAG already exists; publish a new tag (e.g. $TAG.1) instead"; fi

# not a draft, not a pre-release, marked latest: the app reads releases/latest
"$GH" release create "$TAG" "$ZIP" --repo "$REPO" --title "$TAG" --notes-file "$BODY" --latest

# check what the app will read
LATEST=$("$GH" api "repos/$REPO/releases/latest" -q '.tag_name + " " + (.body | test("sha256: '"$SHA"'") | tostring) + " " + ([.assets[].name] | join(","))')
echo "releases/latest: $LATEST"
case "$LATEST" in "$TAG true "*TarkovHipAimer*) echo "OK: apps will be offered $TAG";; *) fail "releases/latest does not show $TAG with the sha256 line and zip; check on github.com";; esac
