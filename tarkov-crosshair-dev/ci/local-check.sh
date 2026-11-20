#!/usr/bin/env bash
# W29 (v5.1) check the CI files on the Linux dev machine before pushing (no network, nothing pushed):
#   1 every ci/*.ps1 parses (PowerShell 7)          2 the workflow passes actionlint
#   3 build.ps1 builds the exe + probe + DpiTool from a source snapshot (0 errors / 0 warnings gate)
#   4 push-to-github.sh --dry-run assembles the repo folder in scratch and runs its safety scan
# Needs pwsh and actionlint: PWSH=/path/to/pwsh ACTIONLINT=/path/to/actionlint bash local-check.sh
# (W29 used the official Linux tarballs from the PowerShell and actionlint GitHub release pages.)
set -uo pipefail
CI="$(cd "$(dirname "$0")" && pwd -P)"
S="${KIROCREW_SCRATCH:-$HOME/.cache}/tarkov-ci-check"; mkdir -p "$S"
PWSH="${PWSH:-$(command -v pwsh || true)}"; AL="${ACTIONLINT:-$(command -v actionlint || true)}"
rc=0
if [ -n "$PWSH" ]; then
  for f in "$CI"/*.ps1; do
    "$PWSH" -NoProfile -c "\$e=\$null; [void][System.Management.Automation.Language.Parser]::ParseFile('$f',[ref]\$null,[ref]\$e); if(\$e){\$e|%{'PARSE FAIL $f '+\$_.Extent.StartLineNumber+': '+\$_.Message}; exit 1} else {'parse ok '+(Split-Path -Leaf '$f')}" || rc=1
  done
else echo "skip 1: no pwsh"; fi
if [ -n "$AL" ]; then "$AL" "$CI/windows-ci.yml" && echo "actionlint ok" || rc=1; else echo "skip 2: no actionlint"; fi
if [ -n "$PWSH" ]; then
  mkdir -p "$S/app" && cp -r /home/dev/tarkov-crosshair-v4/. "$S/app/"
  TARKOV_CI_OUT="$S/out" TARKOV_CI_WORK="$S/work" TARKOV_CI_APP="$S/app" "$PWSH" -NoProfile -File "$CI/build.ps1" || rc=1
fi
TARKOV_CI_STAGE="$S/stage" bash "$CI/push-to-github.sh" --dry-run | tail -2 || rc=1
echo "local check: $([ $rc = 0 ] && echo OK || echo FAILED)"
exit $rc
