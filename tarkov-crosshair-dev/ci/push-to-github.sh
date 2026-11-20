#!/usr/bin/env bash
# W29 (v5.1) copy what the Windows CI needs into one git folder and push it to Leo's PRIVATE GitHub repo.
#   bash push-to-github.sh --dry-run             build the folder, run the safety scan, show what would be pushed, push nothing
#   bash push-to-github.sh <repo address>         first push: e.g. git@github.com:NAME/tarkov-ci.git (saved for next time)
#   bash push-to-github.sh                        later pushes (uses the saved address)
# The folder is ~/tarkov-ci-repo (a normal git folder; delete it any time, the next run makes it again).
# What goes in: tarkov-crosshair-v4 (app source, icon, READMEs), tarkov-shade-module/src + its two LICENSE files, the
# test projects (tests-v4, tests-v42-filtertut, tests-v3/fixtures), the two files the tests read (package-v41.sh,
# v4-requests/LEO-DEFAULT-v5-settings.json), this ci folder, and .github/workflows/windows-ci.yml.
# Never in: backups, staging, built files (bin/obj), logs, settings.json, the network worker / any credentials.
set -euo pipefail
H=/home/dev
DEV=$H/tarkov-crosshair-dev
STAGE=${TARKOV_CI_STAGE:-$H/tarkov-ci-repo}
SAVED=$H/.tarkov-ci-remote
DRY=0; REMOTE=""
for a in "$@"; do case "$a" in --dry-run) DRY=1;; -h|--help) sed -n 2,10p "$0"; exit 0;; *) REMOTE="$a";; esac; done
if [ -z "$REMOTE" ] && [ -f "$SAVED" ]; then REMOTE="$(cat "$SAVED")"; fi
if [ "$DRY" = 0 ] && [ -z "$REMOTE" ]; then echo "Give the repo address the first time, e.g.: bash $0 git@github.com:NAME/tarkov-ci.git"; exit 2; fi

mkdir -p "$STAGE"
STAGE="$(cd "$STAGE" && pwd -P)"  # physical path (/home/dev is a symlink; rsync refuses to follow it)
EX=(--exclude bin/ --exclude obj/ --exclude out/ --exclude '*.log' --exclude 'settings.json' --exclude '数据 Data/' --exclude '__pycache__/')
R() { rsync -a --delete "${EX[@]}" "$@"; }
R $H/tarkov-crosshair-v4/ "$STAGE/tarkov-crosshair-v4/"
mkdir -p "$STAGE/tarkov-shade-module" "$STAGE/tarkov-crosshair-dev/tests-v3" "$STAGE/tarkov-crosshair-dev/v4-requests" "$STAGE/.github/workflows"
R $H/tarkov-shade-module/src/ "$STAGE/tarkov-shade-module/src/"
cp $H/tarkov-shade-module/LICENSE-SHADEPILOT.txt $H/tarkov-shade-module/LICENSE-THIRD-PARTY.txt "$STAGE/tarkov-shade-module/"
R $DEV/tests-v4/ "$STAGE/tarkov-crosshair-dev/tests-v4/"
R $DEV/tests-v42-filtertut/ "$STAGE/tarkov-crosshair-dev/tests-v42-filtertut/"
R $DEV/tests-v3/fixtures/ "$STAGE/tarkov-crosshair-dev/tests-v3/fixtures/"
R $DEV/ci/ "$STAGE/tarkov-crosshair-dev/ci/"
cp $DEV/package-v41.sh "$STAGE/tarkov-crosshair-dev/"
cp $DEV/v4-requests/LEO-DEFAULT-v5-settings.json "$STAGE/tarkov-crosshair-dev/v4-requests/"
cp $DEV/ci/windows-ci.yml "$STAGE/.github/workflows/windows-ci.yml"
# no line-ending conversion anywhere (the tests compare files byte for byte; Windows git would otherwise add CR)
printf '* -text\n' > "$STAGE/.gitattributes"
printf 'ci-out/\nci-work/\nbin/\nobj/\n' > "$STAGE/.gitignore"
cat > "$STAGE/README.md" <<'EOF'
# Tarkov Hip Aimer - Windows CI (private)

Source snapshot for the Windows CI only. Every push to `main` runs `.github/workflows/windows-ci.yml` on a GitHub Windows
machine: build the exe, run the tests, time startup, check the layout at 100/150/200 %. Results: Actions tab -> the run ->
Summary, and the artifact zip at the bottom of the run page. Setup and use: `tarkov-crosshair-dev/ci/CI-SETUP.md`.
EOF

# safety scan: nothing that looks like a key, token or password may leave the machine
PAT='(-----BEGIN [A-Z ]*PRIVATE KEY-----|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|xox[abpr]-[A-Za-z0-9-]{10,}|CLOUDFLARE_API_TOKEN *= *[A-Za-z0-9_-]{20,}|api[_-]?token *[:=] *"[A-Za-z0-9_-]{20,}")'
if grep -rIlE "$PAT" "$STAGE" --exclude-dir=.git >/tmp/.tarkov-ci-scan.$$ 2>/dev/null; then
  echo "STOP: these files contain something that looks like a key or token, nothing was pushed:"; cat /tmp/.tarkov-ci-scan.$$; rm -f /tmp/.tarkov-ci-scan.$$; exit 3
fi
rm -f /tmp/.tarkov-ci-scan.$$
for f in worker NETWORK-SETUP.md .dev.vars wrangler.toml; do
  if [ -n "$(find "$STAGE" -path "$STAGE/.git" -prune -o -name "$f" -print | head -1)" ]; then echo "STOP: $f must not be in the CI repo"; exit 3; fi
done

cd "$STAGE"
[ -d .git ] || { git init -q -b main; }
git add -A
SIZE=$(du -sh --exclude=.git . | cut -f1)
CHANGED=$(git status --porcelain | wc -l)
echo "CI folder: $STAGE ($SIZE, $CHANGED changed files)"
if [ "$DRY" = 1 ]; then git status --short | head -20 || true; echo "dry run: nothing pushed"; exit 0; fi
if [ "$CHANGED" -gt 0 ]; then
  git -c user.name="Leo Liu" -c user.email="dev@users.noreply.github.com" commit -q -m "CI snapshot $(date '+%Y-%m-%d %H:%M')"
fi
if git remote get-url origin >/dev/null 2>&1; then git remote set-url origin "$REMOTE"; else git remote add origin "$REMOTE"; fi
echo "$REMOTE" > "$SAVED"
git push -u origin main
echo "Pushed. Open your repo on github.com -> Actions to watch the run."
