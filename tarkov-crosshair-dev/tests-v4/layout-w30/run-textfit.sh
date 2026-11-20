#!/usr/bin/env bash
# v5.1 W30 text-fit check of every settings page (decision 9) (Mono + Xvfb, see W30TextFitProbe.cs).
#   run-textfit.sh <outdir> [--sizes "2160x1440 1280x720 1920x1080 3840x2160"] [--dpis "96 144 192"]
# Snapshots the CURRENT source, builds it with the render harness's Mono shims, runs the probe once per screen x DPI.
# Prints one line per combo and "W30TEXTFIT pass=N fail=M combos=K"; exit 0 only when every combo ran and nothing failed.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
R=/home/dev/tarkov-crosshair-dev/render
. "$R/env.sh"
OUT="${1:?usage: run-textfit.sh <outdir>}"; shift
SIZES="1280x720 1920x1080 2160x1440"; DPIS="96 144 192"
SRC=/home/dev/tarkov-crosshair-v4/src
while [ $# -gt 0 ]; do case "$1" in --sizes) SIZES="$2"; shift 2;; --dpis) DPIS="$2"; shift 2;; --src) SRC="$2"; shift 2;; *) echo "bad option $1"; exit 2;; esac; done
[ -x "$RENDER_ENV/bin/mono" ] || { echo "W30TEXTFIT SKIP: Mono toolchain missing ($R/setup.sh)"; exit 4; }
mkdir -p "$OUT/_build/stub" "$OUT/_build/.fccache"
OUT="$(cd "$OUT" && pwd)"; B="$OUT/_build"
rm -f "$B"/*.cs "$B"/*.dll "$B"/*.exe "$B"/*.config "$OUT"/res-*.txt 2>/dev/null
cp "$SRC"/*.cs "$B/"; [ -d "$SRC/Shade" ] && for f in "$SRC"/Shade/*.cs; do cp "$f" "$B/Shade_$(basename "$f")"; done
for f in "$SRC"/*.cs; do cmp -s "$f" "$B/$(basename "$f")" || cp "$f" "$B/"; done
python3 "$R/stub/patch_src.py" "$B" > "$B/patch.log" || { echo "W30TEXTFIT FAIL: patch"; exit 3; }
python3 "$R/stub/gen_stub.py" "$B" "$B/stub" "$B/stub/libwinstub.so" >> "$B/patch.log" || { echo "W30TEXTFIT FAIL: stub"; exit 3; }
# v5.1: two DllImports of one function (new Net*/Native code) make gen_stub emit it twice -- keep the first definition
python3 - "$B/stub/winstub.c" <<'PYEOF'
import re, sys
p = sys.argv[1]; seen = set(); out = []
for line in open(p).read().split("\n"):
    m = re.match(r"^\w[\w\s\*]*?\b(\w+)\s*\([^;]*\)\s*\{.*\}\s*$", line)
    if m:
        if m.group(1) in seen: continue
        seen.add(m.group(1))
    out.append(line)
open(p, "w").write("\n".join(out))
PYEOF
gcc -shared -fPIC -O1 -o "$B/stub/libwinstub.so" "$B/stub/winstub.c" || { echo "W30TEXTFIT FAIL: gcc"; exit 3; }
cp "$B/stub/dllmap.config" "$B/TarkovHipAimer.dll.config"; cp "$B/stub/dllmap.config" "$B/RenderDriver.exe.config"
sed -e "s#@ENV@#$RENDER_ENV#g" -e "s#@CACHE@#$B/.fccache#g" "$R/fonts.conf.in" > "$B/fonts.conf"
REFS="-r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Runtime.Serialization.dll -r:System.Xml.dll"
( cd "$B" && csc -nologo -langversion:5 -target:library -out:TarkovHipAimer.dll -codepage:65001 $REFS ./*.cs ) > "$B/build-app.log" 2>&1
[ -e "$B/TarkovHipAimer.dll" ] || { grep -m5 error "$B/build-app.log"; echo "W30TEXTFIT FAIL: app does not compile on Mono"; exit 3; }
( cd "$B" && csc -nologo -target:exe -out:RenderDriver.exe $REFS -r:TarkovHipAimer.dll "$HERE/W30TextFitProbe.cs" ) > "$B/build-probe.log" 2>&1 \
  || { cat "$B/build-probe.log"; echo "W30TEXTFIT FAIL: probe does not compile"; exit 3; }

combo() {
  local SIZE="$1" DPI="$2" W H D DN XPID
  W="${SIZE%x*}"; H="${SIZE#*x}"; D="$OUT/x-$SIZE-$DPI"; mkdir -p "$D"
  echo "Xft.dpi: $DPI" > "$D/.xres"; : > "$D/.display"
  Xvfb -displayfd 3 -screen 0 "${W}x${H}x24" -dpi "$DPI" -nolisten tcp -noreset 3> "$D/.display" > "$D/xvfb.log" 2>&1 &
  XPID=$!
  for i in $(seq 1 100); do DN="$(tr -dc 0-9 < "$D/.display")"; [ -n "$DN" ] && break; sleep 0.1; done
  ( export DISPLAY=":$DN" XENVIRONMENT="$D/.xres" RENDER_DPI="$DPI" FONTCONFIG_FILE="$B/fonts.conf"
    cd "$B" && timeout 400 mono RenderDriver.exe "$OUT/res-$SIZE-$DPI.txt" ) > "$D/log.txt" 2>&1
  kill "$XPID" 2>/dev/null; wait "$XPID" 2>/dev/null
}
export -f combo; export OUT B
for S in $SIZES; do for D in $DPIS; do echo "$S $D"; done; done | xargs -P 6 -L 1 bash -c 'combo "$0" "$1"'

P=0; F=0; N=0; MISSING=0
for S in $SIZES; do for D in $DPIS; do
  N=$((N+1)); f="$OUT/res-$S-$D.txt"
  if [ ! -s "$f" ]; then MISSING=$((MISSING+1)); echo "  $S @${D}dpi: NO RESULT (see $OUT/x-$S-$D/log.txt)"; continue; fi
  l="$(tail -1 "$f")"; p="${l#*pass=}"; p="${p%% *}"; q="${l#*fail=}"; q="${q%% *}"
  P=$((P+p)); F=$((F+q)); echo "  $S @$((D*100/96))%: $l"
done; done
echo "W30TEXTFIT pass=$P fail=$F combos=$N missing=$MISSING"
[ "$F" -eq 0 ] && [ "$MISSING" -eq 0 ]
