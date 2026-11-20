#!/usr/bin/env bash
# Tarkov Hip Aimer v4.1 -- build the READY-BUILT exe and the release zip (PACKAGE41, 17 Nov 2026; started from package-v4.sh).
#
#   bash /home/dev/tarkov-crosshair-dev/package-v41.sh            final run: stops at the first failure and
#                                                                           publishes only when every check passed
#   bash /home/dev/tarkov-crosshair-dev/package-v41.sh --dry-run  same checks, reports every failure, publishes
#                                                                           nothing (zip stays in staging-v41/)
#
# Final output: /home/dev/TarkovHipAimer-v4.1.zip and a copy in /home/dev/slack-out/
#   TarkovHipAimer/
#     准星 TarkovHipAimer.exe            ready-built: double-click, no admin (asInvoker manifest), app icon inside
#     说明 README.txt                     = <v4 tree>/说明 README.txt (UTF-8 BOM, CRLF) = the in-app 指南 (GuideText.cs)
#     准星样式 Crosshairs/
#       说明 README.txt                   three Chinese lines: uploaded PNGs go here
# Not in the zip: bat (no fallback), source, app.ico / app.manifest (both are inside the exe), settings.json, logs.
# The root folder TarkovHipAimer/ is kept from v4.0, so "extract here" never spills the files.
# Exe name: Chinese + English like every package file (SPEC-v4 rule 13; main chat 17 Nov). It is compiled as
# TarkovHipAimer.exe (assembly name TarkovHipAimer) and stored under EXE_ZIP; the app finds settings.json and
# 准星样式 Crosshairs next to the exe whatever the exe is called. The README must name the exe exactly EXE_ZIP.
#
# Steps (logs in /home/dev/tarkov-crosshair-dev/staging-v41/build/):
#   1 gen-guide.py: src/GuideText.cs = 说明 README.txt
#   2 snapshot src/*.cs, src/app.manifest, app.ico and the README into staging-v41/build/tarkov-crosshair-v4/
#   3 compile TarkovHipAimer.exe from the snapshot with the v4.0 bat's csc flags (/codepage:65001 /target:winexe
#     /platform:x64 /optimize+ /win32manifest /win32icon, the same 6 /r:) at C# 5 (/langversion:5) against the .NET
#     Framework 4.8 reference assemblies, with the .NET SDK's Roslyn csc; plus -noconfig -nostdlib and an explicit
#     mscorlib (the Windows csc adds mscorlib by itself) and -deterministic. Any error or warning fails.
#   4 exe: PE32+ AMD64, Windows GUI subsystem, IL-only, entry TarkovCrosshair.Program::Main; manifest resource 1 is
#     src/app.manifest byte for byte (asInvoker, uiAccess false); the group icon holds every frame of app.ico; every
#     type reference, member reference and override resolves in the .NET Framework 4.8 reference assemblies;
#     Program.Version = v4.1; Mono loads every type; no ban/risk word in any string of the exe
#   5 tests-v4, a copy compiled against the same snapshot: TOTAL fail=0
#   6 zip: exact entry list, UTF-8 name flag (0x800) on every entry in the raw local and central headers, CRC test
#     (Python + Info-ZIP unzip), unzip and compare bytes, README checks, wording scan of the unzipped tree
#   7 the live tree still equals the snapshot (nobody saved a file during the run), then publish
set -euo pipefail
DEV=/home/dev/tarkov-crosshair-dev
STAGE="$DEV/staging-v41"
mkdir -p "$STAGE/build" "$STAGE/tools"
exec 9>"$STAGE/.lock"
flock -n 9 || { echo "PACKAGE FAILED: another package-v41.sh run holds $STAGE/.lock" >&2; exit 1; }
exec python3 - "$@" <<'PY'
import hashlib, json, os, re, shutil, struct, subprocess, sys, time, zipfile
import xml.etree.ElementTree as ET

ARGS = sys.argv[1:]
DRY = "--dry-run" in ARGS
if [a for a in ARGS if a != "--dry-run"]:
    sys.exit("usage: bash /home/dev/tarkov-crosshair-dev/package-v41.sh [--dry-run]")

LIVE = "/home/dev/tarkov-crosshair-v4"
DEV = "/home/dev/tarkov-crosshair-dev"
STAGE = DEV + "/staging-v41"
B = STAGE + "/build"
SNAP = B + "/tarkov-crosshair-v4"   # same folder name as the live tree: tests-v4 finds <ancestor>/../tarkov-crosshair-v4/src
TOOLS = STAGE + "/tools"
OUT = "/home/dev/TarkovHipAimer-v4.1.zip"
SLACK_OUT = "/home/dev/slack-out/TarkovHipAimer-v4.1.zip"
DRY_ZIP = STAGE + "/TarkovHipAimer-v4.1.dryrun.zip"
PROGRESS = DEV + "/v4-progress.md"
VERSION = "v4.1"
ZIP_ROOT = "TarkovHipAimer/"
EXE = "TarkovHipAimer.exe"                 # compiler output; assembly name TarkovHipAimer
EXE_ZIP = "准星 " + EXE                     # the name in the zip (Chinese + English)
README = "说明 README.txt"
XH = "准星样式 Crosshairs/"
XH_README = "说明 README.txt"
XH_TEXT = ("准星样式 Crosshairs\r\n"
           "\r\n"
           "这里放你自己上传的准星图片（PNG）。\r\n"
           "在设置的「外观」页选「自定义图片」，点「上传 PNG…」，程序会把图片复制到这里。\r\n"
           "所有准星中间都是空的，包括你上传的图片，游戏中间的小点不会被挡住。\r\n")
DOTNET = "/home/dev/.dotnet/dotnet"
REF = os.path.expanduser("~/.nuget/packages/microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8")
REFS = ["mscorlib.dll", "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll",
        "System.Runtime.Serialization.dll", "System.Xml.dll"]
MONO = os.path.expanduser("~/.local/mm/root/envs/winforms")
BANNED_ZH = ["封号", "封禁", "风险", "保证", "作弊", "外挂"]          # 保证 also covers 不保证
BANNED_EN = re.compile(r"\b(?:ban|bans|banned|banning|risk|risks|risky|cheat\w*|anticheat|battl?eye)\b", re.I)
OPEN_MARK = re.compile(r"\[待[^\]]*\]|\bTODO\b|\bUNVERIFIED\b|待 ?Leo ?确认")
ENV = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1",
           DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
ENV.pop("LIB", None)

problems, warnings, facts = [], [], []
T0 = time.time()


class Stop(Exception):
    """Dry run: a step cannot go on without the output of a failed one."""


def say(s):
    print(s, flush=True)


def fail(step, msg):
    if DRY:
        problems.append("%s: %s" % (step, msg))
        say("  FAIL  %s: %s" % (step, msg))
    else:
        say("PACKAGE FAILED  %s: %s" % (step, msg))
        say("logs: " + B)
        sys.exit(1)


def warn(step, msg):
    warnings.append("%s: %s" % (step, msg))
    say("  WARN  %s: %s" % (step, msg))


def ok(step, msg):
    facts.append("%s: %s" % (step, msg))
    say("  ok    %s: %s" % (step, msg))


def sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def same(a, b):
    return os.path.isfile(a) and os.path.isfile(b) and sha(a) == sha(b)


def run(args, log, cwd=None, env=None, timeout=1800):
    try:
        r = subprocess.run(args, cwd=cwd, env=env or ENV, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                           stderr=subprocess.STDOUT, timeout=timeout)
        out, rc = r.stdout.decode("utf-8", "replace"), r.returncode
    except subprocess.TimeoutExpired as e:
        out, rc = (e.stdout or b"").decode("utf-8", "replace") + "\n[timeout after %d s]" % timeout, -9
    with open(log, "w", encoding="utf-8") as f:
        f.write("$ " + " ".join(args) + "\n\n" + out)
    return rc, out


def write_if_changed(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if os.path.isfile(path) and open(path, encoding="utf-8").read() == text:
        return
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)


def scan_words(step, where, text):
    for w in BANNED_ZH:
        if w in text:
            i = text.index(w)
            fail(step, "%s contains 「%s」: ...%s..." % (where, w, text[max(0, i - 20):i + 20].replace("\n", " ")))
    for m in BANNED_EN.finditer(text):
        i = m.start()
        fail(step, "%s contains '%s': ...%s..." % (where, m.group(0), text[max(0, i - 30):i + 30].replace("\n", " ")))


# ---------------------------------------------------------------------------------------------------------- 1 guide
def step_guide():
    say("1 gen-guide.py (GuideText.cs = 说明 README.txt)")
    rc, out = run(["python3", DEV + "/gen-guide.py"], B + "/gen-guide.log", timeout=120)
    if rc != 0:
        fail("1 gen-guide", "exit %d: %s" % (rc, out.strip()[-600:]))
    else:
        ok("1 gen-guide", "src/GuideText.cs regenerated from 说明 README.txt")


# ------------------------------------------------------------------------------------------------------- 2 snapshot
def live_files():
    src = os.path.join(LIVE, "src")
    cs = sorted(f for f in os.listdir(src) if f.endswith(".cs") and os.path.isfile(os.path.join(src, f)))
    rel = ["src/" + f for f in cs] + ["src/app.manifest", "app.ico", README]
    if os.path.isfile(os.path.join(LIVE, "README.txt")):
        rel.append("README.txt")
    return rel


def step_snapshot():
    say("2 snapshot of the live tree -> " + SNAP)
    missing = [r for r in ("src/Program.cs", "src/app.manifest", "app.ico", README) if not os.path.isfile(os.path.join(LIVE, r))]
    if missing:
        fail("2 snapshot", "missing in %s: %s" % (LIVE, ", ".join(missing)))
        raise Stop()
    os.makedirs(SNAP + "/src", exist_ok=True)
    rel = live_files()
    keep = set(rel)
    for f in os.listdir(SNAP + "/src"):                     # files gone from the live tree (only names copied here earlier)
        if "src/" + f not in keep and os.path.isfile(os.path.join(SNAP, "src", f)):
            os.remove(os.path.join(SNAP, "src", f))
    if "README.txt" not in keep and os.path.isfile(os.path.join(SNAP, "README.txt")):
        os.remove(os.path.join(SNAP, "README.txt"))
    for r in rel:
        shutil.copyfile(os.path.join(LIVE, r), os.path.join(SNAP, r))
    for _ in range(5):                                        # others save files while we copy: re-copy until stable
        changed = [r for r in rel if not same(os.path.join(LIVE, r), os.path.join(SNAP, r))]
        if not changed:
            break
        time.sleep(2)
        for r in changed:
            shutil.copyfile(os.path.join(LIVE, r), os.path.join(SNAP, r))
    hashes = {r: sha(os.path.join(SNAP, r)) for r in rel}
    ok("2 snapshot", "%d .cs files + src/app.manifest + app.ico + README" % sum(1 for r in rel if r.endswith(".cs")))
    # The exe embeds src/app.manifest. The older <tree>/app.manifest is not shipped (compilecheck-v4 and the render
    # harness read it), but no manifest in the tree may ask for admin rights any more.
    levels = {}
    for r in ("src/app.manifest", "app.manifest"):
        p = os.path.join(LIVE, r)
        if os.path.isfile(p):
            m = re.findall(r'requestedExecutionLevel\s+level="([^"]*)"', open(p, encoding="utf-8-sig").read())
            levels[r] = m
            if m != ["asInvoker"]:
                fail("2 snapshot", "%s requestedExecutionLevel is %s, want asInvoker" % (p, m))
    ok("2 snapshot", "requestedExecutionLevel: " + ", ".join("%s %s" % (r, v) for r, v in levels.items()))
    # GuideText.cs in the snapshot must be exactly what gen-guide.py makes from the snapshot README.
    gg = open(DEV + "/gen-guide.py", encoding="utf-8").read()
    a, b = LIVE + "/" + README, LIVE + "/src/GuideText.cs"
    if a not in gg or b not in gg:
        fail("2 snapshot", "gen-guide.py no longer names %s and %s; update package-v41.sh" % (a, b))
    else:
        chk = B + "/GuideText.check.cs"
        exec(compile(gg.replace(a, SNAP + "/" + README).replace(b, chk), DEV + "/gen-guide.py", "exec"), {})
        if same(chk, SNAP + "/src/GuideText.cs"):
            ok("2 snapshot", "GuideText.cs = 说明 README.txt (gen-guide.py output)")
        else:
            fail("2 snapshot", "src/GuideText.cs does not match 说明 README.txt (README saved after gen-guide.py?)")
    return hashes


# -------------------------------------------------------------------------------------------------------- 3 compile
def roslyn():
    rc, out = run([DOTNET, "--list-sdks"], B + "/sdks.log", timeout=120)
    best = None
    for line in out.splitlines():
        m = re.match(r"\s*(\S+) \[(.+)\]\s*$", line)
        if m and os.path.isfile(os.path.join(m.group(2), m.group(1), "Roslyn", "bincore", "csc.dll")):
            best = os.path.join(m.group(2), m.group(1), "Roslyn", "bincore", "csc.dll")
    return best


def step_compile():
    say("3 compile TarkovHipAimer.exe (C# 5, .NET Framework 4.8 refs, x64 WinExe, manifest + icon)")
    csc = roslyn()
    if not csc:
        fail("3 compile", "no Roslyn csc.dll under the .NET SDK of " + DOTNET)
        raise Stop()
    absent = [r for r in REFS if not os.path.isfile(os.path.join(REF, r))]
    if absent:
        fail("3 compile", "missing .NET Framework 4.8 reference assemblies in %s: %s" % (REF, ", ".join(absent)))
        raise Stop()
    os.makedirs(B + "/exe", exist_ok=True)
    exe = B + "/exe/" + EXE
    if os.path.exists(exe):
        os.remove(exe)
    srcs = sorted(os.path.join(SNAP, "src", f) for f in os.listdir(SNAP + "/src") if f.endswith(".cs"))
    args = [DOTNET, "exec", csc, "-noconfig", "-nostdlib+", "-nologo", "-utf8output", "-langversion:5",
            "-codepage:65001", "-target:winexe", "-platform:x64", "-optimize+", "-deterministic", "-warn:4",
            "-win32manifest:" + SNAP + "/src/app.manifest", "-win32icon:" + SNAP + "/app.ico", "-out:" + exe]
    args += ["-r:" + os.path.join(REF, r) for r in REFS] + srcs
    rc, out = run(args, B + "/build.log", timeout=900)
    lines = [l for l in out.splitlines() if l.strip()]
    errs = [l for l in lines if re.search(r"\berror [A-Z]*\d+", l)]
    warns = [l for l in lines if re.search(r"\bwarning [A-Z]*\d+", l)]
    infos = [l.replace(SNAP + "/", "") for l in lines if re.search(r"\binfo [A-Z]*\d+", l)]   # notes, not warnings
    if rc != 0 or errs or warns or not os.path.isfile(exe):
        shown = "\n          ".join(l.replace(SNAP + "/", "") for l in (errs + warns + lines)[:12])
        fail("3 compile", "%d error(s), %d warning(s), exit %d, log %s\n          %s" % (len(errs), len(warns), rc, B + "/build.log", shown))
        raise Stop()
    other = [l for l in lines if not re.search(r"\binfo [A-Z]*\d+", l)]
    if other:
        fail("3 compile", "unexpected compiler output (log %s): %s" % (B + "/build.log", other[0]))
    ver = subprocess.run([DOTNET, "exec", csc, "-version"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=ENV).stdout.decode().strip()
    ok("3 compile", "0 errors, 0 warnings (Roslyn %s, -langversion:5, %d files), %d bytes" % (ver, len(srcs), os.path.getsize(exe)))
    for l in infos:
        say("  note  3 compile (info, not a warning): " + l)
    return exe


# --------------------------------------------------------------------------------------------------------- 4 exe
def pe_resources(data):
    lf, = struct.unpack_from("<I", data, 0x3C)
    if data[lf:lf + 4] != b"PE\0\0":
        raise ValueError("no PE signature")
    nsec, = struct.unpack_from("<H", data, lf + 6)
    optsz, = struct.unpack_from("<H", data, lf + 20)
    opt = lf + 24
    magic, = struct.unpack_from("<H", data, opt)
    dd = opt + (112 if magic == 0x20B else 96)
    rva, _size = struct.unpack_from("<II", data, dd + 8 * 2)
    secs = []
    for i in range(nsec):
        vsize, va, rawsize, rawptr = struct.unpack_from("<IIII", data, opt + optsz + 40 * i + 8)
        secs.append((va, max(vsize, rawsize), rawptr))

    def off(r):
        for va, sz, ptr in secs:
            if va <= r < va + sz:
                return ptr + r - va
        raise ValueError("RVA %#x outside every section" % r)

    out = {}
    if not rva:
        return out
    base = off(rva)

    def walk(o, path):
        if len(path) > 3:
            raise ValueError("resource tree deeper than 3 levels")
        n_named, n_id = struct.unpack_from("<HH", data, base + o + 12)
        for i in range(n_named + n_id):
            nid, ptr = struct.unpack_from("<II", data, base + o + 16 + 8 * i)
            if nid & 0x80000000:
                so = base + (nid & 0x7FFFFFFF)
                ln, = struct.unpack_from("<H", data, so)
                key = data[so + 2:so + 2 + 2 * ln].decode("utf-16-le")
            else:
                key = nid
            if ptr & 0x80000000:
                walk(ptr & 0x7FFFFFFF, path + (key,))
            else:
                drva, dsize = struct.unpack_from("<II", data, base + ptr)
                p = off(drva)
                out[path + (key,)] = data[p:p + dsize]

    walk(0, ())
    return out


def version_strings(blob):
    out = {}

    def parse(o, end, depth):
        while o + 6 <= end:
            ln, vlen, _typ = struct.unpack_from("<HHH", blob, o)
            if ln < 6:
                break
            k = o + 6
            while k + 1 < o + ln and blob[k:k + 2] != b"\0\0":
                k += 2
            key = blob[o + 6:k].decode("utf-16-le", "replace")
            v = (k + 2 + 3) & ~3
            if depth == 0:
                parse((v + vlen + 3) & ~3, o + ln, 1)
            elif depth == 1:
                if key == "StringFileInfo":
                    parse(v, o + ln, 2)
            elif depth == 2:
                parse(v, o + ln, 3)
            else:
                out[key] = blob[v:v + 2 * vlen].decode("utf-16-le", "replace").rstrip("\0") if vlen else ""
            o = (o + ln + 3) & ~3

    parse(0, len(blob), 0)
    return out


def step_resources(exe):
    say("4a exe resources: manifest, icon, version")
    data = open(exe, "rb").read()
    try:
        res = pe_resources(data)
    except Exception as e:
        fail("4 exe", "cannot read the resource section: %s" % e)
        return
    ok("4 exe", "resource types %s (3 icon, 14 group icon, 16 version, 24 manifest)" % sorted(set(k[0] for k in res)))
    man = [(k, v) for k, v in res.items() if k[0] == 24]
    if len(man) != 1 or man[0][0][1] != 1:
        fail("4 exe", "expected exactly one manifest resource with ID 1, found %s" % [k for k, _ in man])
    else:
        mb = man[0][1]
        src = open(SNAP + "/src/app.manifest", "rb").read()
        if mb != src:
            fail("4 exe", "embedded manifest differs from src/app.manifest (%d vs %d bytes)" % (len(mb), len(src)))
        try:
            root = ET.fromstring(mb)
            lv = root.findall(".//{urn:schemas-microsoft-com:asm.v3}requestedExecutionLevel")
            dpi = [e.text for e in root.findall(".//{http://schemas.microsoft.com/SMI/2005/WindowsSettings}dpiAware")]
            dpi2 = [e.text for e in root.findall(".//{http://schemas.microsoft.com/SMI/2016/WindowsSettings}dpiAwareness")]
            if len(lv) != 1 or lv[0].get("level") != "asInvoker" or lv[0].get("uiAccess") != "false":
                fail("4 exe", "manifest execution level is %s, want exactly one asInvoker with uiAccess false"
                     % [(e.get("level"), e.get("uiAccess")) for e in lv])
            else:
                ok("4 exe", "manifest resource ID 1 = src/app.manifest: requestedExecutionLevel asInvoker, uiAccess false, "
                   "dpiAware %s%s" % (dpi, (", dpiAwareness %s" % dpi2) if dpi2 else ""))
        except ET.ParseError as e:
            fail("4 exe", "embedded manifest is not valid XML: %s" % e)
    ico = open(SNAP + "/app.ico", "rb").read()
    _r, _t, cnt = struct.unpack_from("<HHH", ico, 0)
    frames = []
    for i in range(cnt):
        w, h, _cc, _rs, _pl, bpp, nbytes, offs = struct.unpack_from("<BBBBHHII", ico, 6 + 16 * i)
        frames.append((w or 256, h or 256, nbytes, ico[offs:offs + nbytes], bpp))
    groups = [(k, v) for k, v in res.items() if k[0] == 14]
    icons = {k[1]: v for k, v in res.items() if k[0] == 3}
    if len(groups) != 1:
        fail("4 exe", "expected one group icon, found %d" % len(groups))
    else:
        g = groups[0][1]
        _r, _t, gc = struct.unpack_from("<HHH", g, 0)
        bad = []
        for i in range(gc):
            w, h, _cc, _rs, _pl, bpp, nbytes, nid = struct.unpack_from("<BBBBHHIH", g, 6 + 14 * i)
            if i >= len(frames) or (w or 256, h or 256, nbytes) != frames[i][:3] or icons.get(nid) != frames[i][3]:
                bad.append(i)
        if gc != cnt or bad:
            fail("4 exe", "group icon %s has %d frames, app.ico %d; frames that differ: %s" % (groups[0][0][1], gc, cnt, bad))
        else:
            ok("4 exe", "group icon (resource %s) = all %d frames of app.ico byte for byte (%s px)"
               % (groups[0][0][1], cnt, "/".join(str(f[0]) for f in frames)))
    ver = [v for k, v in res.items() if k[0] == 16]
    if ver:
        vs = version_strings(ver[0])
        ok("4 exe", "version resource (made by csc, no assembly attributes in the source, as with the bat): "
           + ", ".join("%s=%r" % (k, vs.get(k, "")) for k in ("FileDescription", "FileVersion", "ProductVersion", "OriginalFilename")))
    for w in BANNED_ZH:
        for enc in ("utf-16-le", "utf-8"):
            i = data.find(w.encode(enc))
            if i >= 0:
                fail("4 exe wording", "raw bytes of 「%s」 (%s) at file offset %#x" % (w, enc, i))


VERIFY_CSPROJ = r'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>Verify</AssemblyName>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
'''

VERIFY_CS = r'''// Written by package-v41.sh (PACKAGE41). Offline check of a .NET Framework exe against reference assemblies:
//   dotnet Verify.dll <exe> <refdir> <expected Program.Version> <strings-out.txt>
// Prints one line per check and a last line "VERIFY {json}". Exit 0 = no problem found.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

sealed class Asm
{
    public string Name = "";
    public MetadataReader Md;
    public readonly Dictionary<string, TypeDefinitionHandle> Top = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
    public readonly Dictionary<string, AssemblyReferenceHandle> Forward = new Dictionary<string, AssemblyReferenceHandle>(StringComparer.Ordinal);
}

struct TD
{
    public Asm A;
    public TypeDefinitionHandle H;
    public string[] Args;     // generic arguments of this use of the type (null = the definition's own !0, !1 ...)
    public bool Ok { get { return A != null; } }
}

sealed class Ctx { public string[] Args; }

sealed class Prov : ISignatureTypeProvider<string, Ctx>
{
    public static readonly Prov I = new Prov();
    public string GetArrayType(string e, ArrayShape s) { return e + "[" + new string(',', Math.Max(0, s.Rank - 1)) + "]"; }
    public string GetByReferenceType(string e) { return e + "&"; }
    public string GetFunctionPointerType(MethodSignature<string> s) { return "fnptr " + s.ReturnType + "(" + string.Join(",", s.ParameterTypes) + ")"; }
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) { return g + "<" + string.Join(",", a) + ">"; }
    public string GetGenericMethodParameter(Ctx c, int i) { return "!!" + i; }
    public string GetGenericTypeParameter(Ctx c, int i) { return c != null && c.Args != null && i < c.Args.Length ? c.Args[i] : "!" + i; }
    public string GetModifiedType(string m, string u, bool req) { return u + (req ? " modreq(" : " modopt(") + m + ")"; }
    public string GetPinnedType(string e) { return e + " pinned"; }
    public string GetPointerType(string e) { return e + "*"; }
    public string GetPrimitiveType(PrimitiveTypeCode c) { return "System." + c; }
    public string GetSZArrayType(string e) { return e + "[]"; }
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { return V.Name(r, h); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) { return V.Name(r, h); }
    public string GetTypeFromSpecification(MetadataReader r, Ctx c, TypeSpecificationHandle h, byte k) { return r.GetTypeSpecification(h).DecodeSignature(this, c); }
}

static class V
{
    static readonly Dictionary<string, Asm> Refs = new Dictionary<string, Asm>(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> Problems = new List<string>();
    static readonly List<PEReader> Keep = new List<PEReader>();

    public static string Name(MetadataReader r, TypeDefinitionHandle h)
    {
        TypeDefinition t = r.GetTypeDefinition(h);
        string n = r.GetString(t.Name);
        TypeDefinitionHandle d = t.GetDeclaringType();
        if (!d.IsNil) return Name(r, d) + "/" + n;
        return Key(r.GetString(t.Namespace), n);
    }

    public static string Name(MetadataReader r, TypeReferenceHandle h)
    {
        TypeReference t = r.GetTypeReference(h);
        string n = r.GetString(t.Name);
        if (t.ResolutionScope.Kind == HandleKind.TypeReference) return Name(r, (TypeReferenceHandle)t.ResolutionScope) + "/" + n;
        return Key(r.GetString(t.Namespace), n);
    }

    static string Key(string ns, string n) { return ns.Length == 0 ? n : ns + "." + n; }

    static Asm Open(PEReader pe, string fallbackName)
    {
        Asm a = new Asm();
        a.Md = pe.GetMetadataReader();
        a.Name = a.Md.IsAssembly ? a.Md.GetString(a.Md.GetAssemblyDefinition().Name) : fallbackName;
        foreach (TypeDefinitionHandle h in a.Md.TypeDefinitions)
        {
            TypeDefinition t = a.Md.GetTypeDefinition(h);
            if (t.GetDeclaringType().IsNil) a.Top[Key(a.Md.GetString(t.Namespace), a.Md.GetString(t.Name))] = h;
        }
        foreach (ExportedTypeHandle h in a.Md.ExportedTypes)
        {
            ExportedType e = a.Md.GetExportedType(h);
            if (e.Implementation.Kind == HandleKind.AssemblyReference)
                a.Forward[Key(a.Md.GetString(e.Namespace), a.Md.GetString(e.Name))] = (AssemblyReferenceHandle)e.Implementation;
        }
        return a;
    }

    static TD Top(string asm, string key, int depth)
    {
        Asm a;
        if (depth > 8 || !Refs.TryGetValue(asm, out a)) return default(TD);
        TypeDefinitionHandle h;
        if (a.Top.TryGetValue(key, out h)) return new TD { A = a, H = h };
        AssemblyReferenceHandle fw;
        if (a.Forward.TryGetValue(key, out fw)) return Top(a.Md.GetString(a.Md.GetAssemblyReference(fw).Name), key, depth + 1);
        return default(TD);
    }

    static TD Resolve(Asm from, TypeReferenceHandle h)
    {
        TypeReference t = from.Md.GetTypeReference(h);
        string ns = from.Md.GetString(t.Namespace), n = from.Md.GetString(t.Name);
        EntityHandle s = t.ResolutionScope;
        switch (s.Kind)
        {
            case HandleKind.AssemblyReference:
                return Top(from.Md.GetString(from.Md.GetAssemblyReference((AssemblyReferenceHandle)s).Name), Key(ns, n), 0);
            case HandleKind.TypeReference:
            {
                TD o = Resolve(from, (TypeReferenceHandle)s);
                if (!o.Ok) return default(TD);
                foreach (TypeDefinitionHandle nh in o.A.Md.GetTypeDefinition(o.H).GetNestedTypes())
                    if (o.A.Md.GetString(o.A.Md.GetTypeDefinition(nh).Name) == n) return new TD { A = o.A, H = nh };
                return default(TD);
            }
            case HandleKind.ModuleDefinition:
            {
                TypeDefinitionHandle d;
                return from.Top.TryGetValue(Key(ns, n), out d) ? new TD { A = from, H = d } : default(TD);
            }
            default:
                return default(TD);
        }
    }

    /// <summary>TypeDef / TypeRef / generic TypeSpec -> the type definition (+ the instantiation's arguments, decoded in ctx).</summary>
    static TD Def(Asm a, EntityHandle h, Ctx ctx)
    {
        switch (h.Kind)
        {
            case HandleKind.TypeDefinition: return new TD { A = a, H = (TypeDefinitionHandle)h };
            case HandleKind.TypeReference: return Resolve(a, (TypeReferenceHandle)h);
            case HandleKind.TypeSpecification:
            {
                BlobReader br = a.Md.GetBlobReader(a.Md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
                if (br.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return default(TD);
                br.ReadSignatureTypeCode();                  // CLASS / VALUETYPE
                EntityHandle g = br.ReadTypeHandle();
                int n = br.ReadCompressedInteger();
                SignatureDecoder<string, Ctx> dec = new SignatureDecoder<string, Ctx>(Prov.I, a.Md, ctx);
                string[] args = new string[n];
                for (int i = 0; i < n; i++) args[i] = dec.DecodeType(ref br);
                TD r = Def(a, g, ctx);
                r.Args = args;
                return r;
            }
            default: return default(TD);
        }
    }

    static TD Base(TD t)
    {
        EntityHandle b = t.A.Md.GetTypeDefinition(t.H).BaseType;
        if (b.IsNil) return default(TD);
        return Def(t.A, b, new Ctx { Args = t.Args });
    }

    static string Sig(MethodSignature<string> s)
    {
        IEnumerable<string> ps = s.Header.CallingConvention == SignatureCallingConvention.VarArgs
            ? s.ParameterTypes.Take(s.RequiredParameterCount) : s.ParameterTypes;
        return (s.Header.IsInstance ? "instance " : "static ") + (s.GenericParameterCount > 0 ? "<" + s.GenericParameterCount + "> " : "")
            + s.ReturnType + "(" + string.Join(",", ps) + ")";
    }

    /// <summary>Member with this name and signature on t or a base type (the runtime's MemberRef lookup).</summary>
    static bool Has(TD t, string name, bool method, string sig, bool virtualOnly)
    {
        for (int i = 0; i < 40 && t.Ok; i++, t = Base(t))
        {
            MetadataReader md = t.A.Md;
            TypeDefinition td = md.GetTypeDefinition(t.H);
            Ctx ctx = new Ctx { Args = t.Args };
            if (method)
            {
                foreach (MethodDefinitionHandle mh in td.GetMethods())
                {
                    MethodDefinition m = md.GetMethodDefinition(mh);
                    if (virtualOnly && (m.Attributes & MethodAttributes.Virtual) == 0) continue;
                    if (md.GetString(m.Name) == name && Sig(m.DecodeSignature(Prov.I, ctx)) == sig) return true;
                }
            }
            else
            {
                foreach (FieldDefinitionHandle fh in td.GetFields())
                {
                    FieldDefinition f = md.GetFieldDefinition(fh);
                    if (md.GetString(f.Name) == name && f.DecodeSignature(Prov.I, ctx) == sig) return true;
                }
            }
        }
        return false;
    }

    static string TypeText(MetadataReader md, EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.TypeDefinition: return Name(md, (TypeDefinitionHandle)h);
            case HandleKind.TypeReference: return Name(md, (TypeReferenceHandle)h);
            case HandleKind.TypeSpecification: return md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(Prov.I, new Ctx());
            default: return h.Kind.ToString();
        }
    }

    static void Check(bool good, string what)
    {
        Console.WriteLine((good ? "ok  " : "BAD ") + what);
        if (!good) Problems.Add(what);
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string exePath = args[0], refDir = args[1], wantVersion = args[2], stringsOut = args[3];
        foreach (string dir in new[] { refDir, Path.Combine(refDir, "Facades") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string f in Directory.GetFiles(dir, "*.dll"))
            {
                try
                {
                    PEReader rp = new PEReader(File.OpenRead(f));
                    if (!rp.HasMetadata) { rp.Dispose(); continue; }
                    Keep.Add(rp);
                    Asm ra = Open(rp, Path.GetFileNameWithoutExtension(f));
                    if (!Refs.ContainsKey(ra.Name)) Refs[ra.Name] = ra;
                }
                catch (BadImageFormatException) { }
            }
        }

        PEReader pe = new PEReader(File.OpenRead(exePath));
        Keep.Add(pe);
        PEHeaders hd = pe.PEHeaders;
        Check(hd.IsExe, "image is an exe (not a dll)");
        Check(hd.PEHeader != null && hd.PEHeader.Magic == PEMagic.PE32Plus, "PE32+ (64-bit image)");
        Check(hd.CoffHeader.Machine == Machine.Amd64, "machine AMD64 (x64)");
        Check(hd.PEHeader != null && hd.PEHeader.Subsystem == Subsystem.WindowsGui, "subsystem Windows GUI (no console window)");
        Check(hd.CorHeader != null, "CLI header present (.NET assembly)");
        if (hd.CorHeader == null) { Console.WriteLine("VERIFY " + JsonSerializer.Serialize(new { problems = Problems })); return 1; }
        CorFlags cf = hd.CorHeader.Flags;
        Check((cf & CorFlags.ILOnly) != 0, "IL-only");
        Check((cf & (CorFlags.Requires32Bit | CorFlags.Prefers32Bit)) == 0, "no 32-bit requirement");
        Check((cf & CorFlags.StrongNameSigned) == 0 || true, "strong-name flag " + ((cf & CorFlags.StrongNameSigned) != 0));

        Asm exe = Open(pe, Path.GetFileNameWithoutExtension(exePath));
        MetadataReader md = exe.Md;
        AssemblyDefinition ad = md.GetAssemblyDefinition();
        Check(md.GetString(ad.Name) == "TarkovHipAimer", "assembly name TarkovHipAimer (is " + md.GetString(ad.Name) + ")");
        Check(md.MetadataVersion == "v4.0.30319", "runtime version " + md.MetadataVersion + " (CLR 4 = .NET Framework 4.x)");

        // entry point
        EntityHandle ep = MetadataTokens.EntityHandle(hd.CorHeader.EntryPointTokenOrRelativeVirtualAddress);
        string epName = "?";
        if (ep.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition m = md.GetMethodDefinition((MethodDefinitionHandle)ep);
            epName = Name(md, m.GetDeclaringType()) + "::" + md.GetString(m.Name);
        }
        Check(epName == "TarkovCrosshair.Program::Main", "entry point " + epName);

        // assembly references must all be in the reference set with the same version
        List<string> asmRefs = new List<string>();
        foreach (AssemblyReferenceHandle h in md.AssemblyReferences)
        {
            AssemblyReference ar = md.GetAssemblyReference(h);
            string n = md.GetString(ar.Name), v = ar.Version.ToString();
            Asm ra;
            bool have = Refs.TryGetValue(n, out ra);
            string rv = have ? ra.Md.GetAssemblyDefinition().Version.ToString() : "";
            asmRefs.Add(n + " " + v);
            if (!have) Problems.Add("assembly reference " + n + " " + v + " is not a .NET Framework 4.8 reference assembly");
            else if (rv != v) Problems.Add("assembly reference " + n + " " + v + " but the reference assembly is " + rv);
        }
        Console.WriteLine("ok  assembly references: " + string.Join(", ", asmRefs));

        // every type reference
        int tr = 0, trOk = 0;
        foreach (TypeReferenceHandle h in md.TypeReferences)
        {
            tr++;
            if (Resolve(exe, h).Ok) trOk++;
            else Problems.Add("type not in the .NET Framework 4.8 reference assemblies: " + Name(md, h));
        }
        Check(tr == trOk, "type references resolved: " + trOk + "/" + tr);

        // every member reference (methods + fields the exe calls in the framework)
        int mr = 0, mrOk = 0, skipped = 0;
        foreach (MemberReferenceHandle h in md.MemberReferences)
        {
            MemberReference m = md.GetMemberReference(h);
            EntityHandle p = m.Parent;
            if (p.Kind != HandleKind.TypeReference && p.Kind != HandleKind.TypeSpecification) { skipped++; continue; }
            if (p.Kind == HandleKind.TypeSpecification)
            {
                BlobReader br = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)p).Signature);
                SignatureTypeCode code = br.ReadSignatureTypeCode();
                if (code != SignatureTypeCode.GenericTypeInstance) { skipped++; continue; }   // array methods come from the runtime
            }
            mr++;
            string name = md.GetString(m.Name);
            bool isMethod = m.GetKind() == MemberReferenceKind.Method;
            Ctx none = new Ctx();
            string sig = isMethod ? Sig(m.DecodeMethodSignature(Prov.I, none)) : m.DecodeFieldSignature(Prov.I, none);
            TD owner = Def(exe, p, none);
            owner.Args = null;                         // member-ref signatures use the definition's own !0, !1 ...
            if (!owner.Ok) { Problems.Add("member on an unresolved type: " + TypeText(md, p) + "::" + name); continue; }
            if (Has(owner, name, isMethod, sig, false)) mrOk++;
            else Problems.Add("member not in the .NET Framework 4.8 reference assemblies: " + TypeText(md, p) + "::" + name + " " + sig);
        }
        Check(mr == mrOk, "member references resolved: " + mrOk + "/" + mr + " (" + skipped + " array/local skipped)");

        // every override (virtual, not newslot) must find a virtual base method with the same signature
        int ov = 0, ovOk = 0, types = 0, methods = 0;
        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
        {
            types++;
            TypeDefinition td = md.GetTypeDefinition(th);
            TD self = new TD { A = exe, H = th };
            foreach (MethodDefinitionHandle mh in td.GetMethods())
            {
                methods++;
                MethodDefinition m = md.GetMethodDefinition(mh);
                if ((m.Attributes & MethodAttributes.Virtual) == 0 || (m.Attributes & MethodAttributes.NewSlot) != 0) continue;
                ov++;
                string name = md.GetString(m.Name);
                string sig = Sig(m.DecodeSignature(Prov.I, new Ctx()));
                TD b = Base(self);
                if (b.Ok && Has(b, name, true, sig, true)) ovOk++;
                else Problems.Add("override without a base method in .NET Framework 4.8: " + Name(md, th) + "::" + name + " " + sig);
            }
        }
        Check(ov == ovOk, "overrides resolved: " + ovOk + "/" + ov + " (" + types + " types, " + methods + " methods)");

        // TargetFrameworkAttribute (the bat build had none; csc does not add one)
        string tfm = "none";
        foreach (CustomAttributeHandle ch in ad.GetCustomAttributes())
        {
            CustomAttribute ca = md.GetCustomAttribute(ch);
            if (ca.Constructor.Kind != HandleKind.MemberReference) continue;
            EntityHandle parent = md.GetMemberReference((MemberReferenceHandle)ca.Constructor).Parent;
            if (parent.Kind == HandleKind.TypeReference && Name(md, (TypeReferenceHandle)parent) == "System.Runtime.Versioning.TargetFrameworkAttribute") tfm = "present";
        }
        Console.WriteLine("ok  TargetFrameworkAttribute: " + tfm);

        // Program.Version constant + every string in the exe (#US heap and string constants) for the wording scan
        string version = null;
        List<string> all = new List<string>();
        int rows = md.GetTableRowCount(TableIndex.Constant);
        for (int i = 1; i <= rows; i++)
        {
            Constant c = md.GetConstant(MetadataTokens.ConstantHandle(i));
            if (c.TypeCode != ConstantTypeCode.String) continue;
            BlobReader br = md.GetBlobReader(c.Value);
            string s = br.Length == 0 ? "" : br.ReadUTF16(br.Length);
            all.Add(s);
            if (c.Parent.Kind == HandleKind.FieldDefinition)
            {
                FieldDefinition f = md.GetFieldDefinition((FieldDefinitionHandle)c.Parent);
                if (md.GetString(f.Name) == "Version" && Name(md, f.GetDeclaringType()) == "TarkovCrosshair.Program") version = s;
            }
        }
        Check(version == wantVersion, "Program.Version = " + (version ?? "(missing)") + " (want " + wantVersion + ")");
        int heap = md.GetHeapSize(HeapIndex.UserString), us = 0;
        if (heap > 1)
        {
            UserStringHandle u = MetadataTokens.UserStringHandle(1);
            while (!u.IsNil && MetadataTokens.GetHeapOffset(u) < heap)
            {
                string s = md.GetUserString(u);
                if (s.Length > 0) { all.Add(s); us++; }
                u = md.GetNextHandle(u);
            }
        }
        File.WriteAllLines(stringsOut, all.Select(s => s.Replace("\r", "").Replace("\n", " \u23CE ")), new UTF8Encoding(false));
        Console.WriteLine("ok  strings dumped: " + us + " user strings + " + (all.Count - us) + " string constants -> " + stringsOut);

        foreach (string p in Problems.Distinct().Take(60)) Console.WriteLine("PROBLEM " + p);
        Console.WriteLine("VERIFY " + JsonSerializer.Serialize(new
        {
            typeRefs = tr, typeRefsOk = trOk, memberRefs = mr, memberRefsOk = mrOk, overrides = ov, overridesOk = ovOk,
            types = types, methods = methods, asmRefs = asmRefs, version = version, tfm = tfm, userStrings = us,
            subsystemVersion = hd.PEHeader.MajorSubsystemVersion + "." + hd.PEHeader.MinorSubsystemVersion,
            dllCharacteristics = hd.PEHeader.DllCharacteristics.ToString(), problems = Problems.Distinct().ToList()
        }));
        return Problems.Count == 0 ? 0 : 1;
    }
}
'''

MONOLOAD_CS = r'''// Written by package-v41.sh (PACKAGE41): load the exe in Mono and reflect over every type, method and field.
using System;
using System.Collections.Generic;
using System.Reflection;

static class MonoLoad
{
    static int Main(string[] a)
    {
        Assembly asm = Assembly.LoadFrom(a[0]);
        Type[] types;
        List<string> errs = new List<string>();
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e)
        {
            List<Type> got = new List<Type>();
            foreach (Type t in e.Types) if (t != null) got.Add(t);
            types = got.ToArray();
            foreach (Exception le in e.LoaderExceptions) errs.Add("GetTypes: " + le.GetType().Name + ": " + le.Message);
        }
        int methods = 0, fields = 0;
        const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (Type t in types)
        {
            try
            {
                Type bt = t.BaseType;
                t.GetInterfaces();
                foreach (MethodInfo m in t.GetMethods(all)) { methods++; m.GetParameters(); Type r = m.ReturnType; }
                foreach (ConstructorInfo c in t.GetConstructors(all)) { methods++; c.GetParameters(); }
                foreach (FieldInfo f in t.GetFields(all)) { fields++; Type ft = f.FieldType; }
            }
            catch (Exception ex) { errs.Add(t.FullName + ": " + ex.GetType().Name + ": " + ex.Message); }
        }
        Console.WriteLine("MONO types=" + types.Length + " methods=" + methods + " fields=" + fields + " errors=" + errs.Count);
        foreach (string s in errs) Console.WriteLine("MONOERR " + s);
        return errs.Count == 0 ? 0 : 1;
    }
}
'''


def step_verify(exe):
    say("4b exe metadata: PE headers, references against the .NET Framework 4.8 reference assemblies, strings")
    vdir = TOOLS + "/verify"
    write_if_changed(vdir + "/Verify.csproj", VERIFY_CSPROJ)
    write_if_changed(vdir + "/Verify.cs", VERIFY_CS)
    rc, out = run([DOTNET, "build", vdir + "/Verify.csproj", "-c", "Release", "-o", B + "/verify-bin", "-nologo", "-v", "q"],
                  B + "/verify-build.log", timeout=900)
    if rc != 0 or not os.path.isfile(B + "/verify-bin/Verify.dll"):
        fail("4 exe", "the metadata checker did not build (log %s)" % (B + "/verify-build.log"))
        return None
    strings = B + "/exe-strings.txt"
    rc, out = run([DOTNET, B + "/verify-bin/Verify.dll", exe, REF, VERSION, strings], B + "/verify.log", timeout=600)
    j = None
    for line in out.splitlines():
        if line.startswith("VERIFY "):
            j = json.loads(line[7:])
    if j is None:
        fail("4 exe", "metadata checker gave no result, exit %d (log %s)" % (rc, B + "/verify.log"))
        return None
    for line in out.splitlines():
        if line.startswith("BAD "):
            fail("4 exe", line[4:])
    for p in j["problems"]:
        if not any(p == l[4:] for l in out.splitlines() if l.startswith("BAD ")):
            fail("4 exe", p)
    if rc == 0 and not j["problems"]:
        ok("4 exe", "PE32+ AMD64, Windows GUI subsystem %s, IL-only, entry TarkovCrosshair.Program::Main, %s"
           % (j["subsystemVersion"], j["dllCharacteristics"]))
        ok("4 exe", "resolve in .NET Framework 4.8 refs: %d/%d type refs, %d/%d member refs, %d/%d overrides (%d types, %d methods)"
           % (j["typeRefsOk"], j["typeRefs"], j["memberRefsOk"], j["memberRefs"], j["overridesOk"], j["overrides"], j["types"], j["methods"]))
        ok("4 exe", "assembly refs %s; TargetFrameworkAttribute %s; Program.Version %s"
           % (", ".join(j["asmRefs"]), j["tfm"], j["version"]))
    text = open(strings, encoding="utf-8").read() if os.path.isfile(strings) else ""
    n_before = len(problems)
    scan_words("4 exe wording", "a string in the exe", text)
    if len(problems) == n_before:
        ok("4 exe wording", "%d strings in the exe: no ban/risk word" % (text.count("\n")))
    return j


def step_mono(exe):
    say("4c Mono: load the exe and reflect over every type")
    if not os.path.isfile(MONO + "/bin/mono"):
        fail("4 mono", "Mono not found at %s (render harness toolchain)" % MONO)
        return
    env = dict(ENV, PATH=MONO + "/bin:" + ENV.get("PATH", ""), LD_LIBRARY_PATH=MONO + "/lib", LANG="C.UTF-8", LC_ALL="C.UTF-8",
               MONO_WINFORMS_XIM_STYLE="disabled")
    write_if_changed(TOOLS + "/monoload/MonoLoad.cs", MONOLOAD_CS)
    os.makedirs(B + "/monoload", exist_ok=True)
    rc, out = run([MONO + "/bin/csc", "-nologo", "-out:" + B + "/monoload/MonoLoad.exe", TOOLS + "/monoload/MonoLoad.cs"],
                  B + "/monoload-build.log", env=env, timeout=600)
    if rc != 0:
        fail("4 mono", "MonoLoad.cs did not compile (log %s)" % (B + "/monoload-build.log"))
        return
    rc, out = run([MONO + "/bin/mono", B + "/monoload/MonoLoad.exe", exe], B + "/mono.log", env=env, timeout=600)
    m = re.search(r"MONO types=(\d+) methods=(\d+) fields=(\d+) errors=(\d+)", out)
    if rc != 0 or not m or m.group(4) != "0":
        errs = [l[8:] for l in out.splitlines() if l.startswith("MONOERR ")]
        fail("4 mono", "exit %d, %s (log %s)\n          %s" % (rc, m.group(0) if m else "no result", B + "/mono.log", "\n          ".join(errs[:10])))
    else:
        ok("4 mono", "Mono %s loaded all %s types (%s methods, %s fields), no load error"
           % (mono_version(env), m.group(1), m.group(2), m.group(3)))


def mono_version(env):
    try:
        r = subprocess.run([MONO + "/bin/mono", "--version"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
        mm = re.search(r"version (\S+)", r.stdout.decode())
        return mm.group(1) if mm else "?"
    except Exception:
        return "?"


# ---------------------------------------------------------------------------------------------------------- 5 tests
def sync_tests():
    src, dst = DEV + "/tests-v4", B + "/tests"
    skip = {"bin", "obj"}
    want = set()
    for root, dirs, files in os.walk(src):
        rel = os.path.relpath(root, src)
        if rel == ".":
            dirs[:] = [d for d in dirs if d not in skip]
        for f in files:
            r = os.path.normpath(os.path.join(rel, f))
            want.add(r)
            if r == "Tests.csproj":
                continue
            s, d = os.path.join(src, r), os.path.join(dst, r)
            os.makedirs(os.path.dirname(d), exist_ok=True)
            if not same(s, d):
                shutil.copyfile(s, d)
    for root, dirs, files in os.walk(dst):
        rel = os.path.relpath(root, dst)
        if rel == ".":
            dirs[:] = [d for d in dirs if d not in skip]
        for f in files:
            r = os.path.normpath(os.path.join(rel, f))
            if r not in want:
                os.remove(os.path.join(dst, r))
    raw = open(src + "/Tests.csproj", "rb").read()
    text = raw.decode("utf-8-sig")
    new = text.replace("../../tarkov-crosshair-v4/", "../tarkov-crosshair-v4/").replace("..\\..\\tarkov-crosshair-v4\\", "..\\tarkov-crosshair-v4\\")
    if "../../" in new or "..\\..\\" in new:
        fail("5 tests", "tests-v4/Tests.csproj has a ../../ path the snapshot copy cannot follow")
    data = (b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + new.encode("utf-8")
    p = dst + "/Tests.csproj"
    if not os.path.isfile(p) or open(p, "rb").read() != data:
        with open(p, "wb") as f:
            f.write(data)
    return {r: sha(os.path.join(src, r)) for r in want}


def step_tests():
    say("5 tests-v4 against the snapshot (copy in %s)" % (B + "/tests"))
    th = sync_tests()
    os.makedirs(B + "/test-out", exist_ok=True)
    t = time.time()
    rc, out = run([DOTNET, "run", "-c", "Release", "--", B + "/test-out"], B + "/tests.log", cwd=B + "/tests", timeout=2400)
    m = re.search(r"TOTAL pass=(\d+) fail=(\d+)(?: skip\S*=(\d+))?", out)
    fails = [l for l in out.splitlines() if l.startswith("FAIL ")]
    if rc != 0 or not m or m.group(2) != "0":
        build_errs = [l for l in out.splitlines() if re.search(r"\berror [A-Z]+\d+", l)]
        detail = build_errs[:8] if build_errs and not m else fails[:15]
        fail("5 tests", "%s, exit %d, %.0f s (log %s)\n          %s"
             % (m.group(0) if m else "no TOTAL line (build failed?)", rc, time.time() - t, B + "/tests.log",
                "\n          ".join(l.replace(B + "/", "")[:300] for l in detail)))
    else:
        ok("5 tests", "tests-v4 %s, %.0f s" % (m.group(0), time.time() - t))
    return th


# ------------------------------------------------------------------------------------------------------------ 6 zip
class UZipInfo(zipfile.ZipInfo):
    """Sets the UTF-8 name flag 0x800 on every entry, ASCII names included (checked below from the raw headers)."""

    def _encodeFilenameFlags(self):
        return self.filename.encode("utf-8"), self.flag_bits | 0x800


def readme_bytes():
    raw = open(SNAP + "/" + README, "rb").read()
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    text = raw.decode("utf-8").replace("\r\n", "\n").replace("\r", "\n")
    return text, b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode("utf-8")


def step_zip(exe):
    say("6 zip")
    text, rd = readme_bytes()
    for n in (EXE_ZIP, README, XH.rstrip("/")):
        if n not in text:
            fail("6 README", "README does not name 「%s」 (its file list must match the zip)" % n)
    prefix = EXE_ZIP[:-len(EXE)]
    bare = sorted(set(text.count("\n", 0, m.start()) + 1 for m in re.finditer(re.escape(EXE), text)
                      if not text[:m.start()].endswith(prefix)))
    if bare:
        fail("6 README", "README lines %s name 「%s」 without 「%s」; the zip ships 「%s」"
             % (", ".join(map(str, bare)), EXE, prefix.strip(), EXE_ZIP))
    for gone in (".bat", "程序 app", "Start-HipAimer"):
        if gone in text:
            fail("6 README", "README still mentions 「%s」, which v4.1 does not ship" % gone)
    first = text.split("\n", 1)[0]
    if VERSION not in first:
        fail("6 README", "README first line has no %s: %s" % (VERSION, first.strip()))
    n0 = len(problems)
    scan_words("6 wording", "说明 README.txt", text)
    scan_words("6 wording", XH + XH_README, XH_TEXT)
    opens = OPEN_MARK.findall(text)
    if opens:
        (warn if DRY else fail)("6 README", "open placeholder(s): " + "; ".join(opens[:5]))
    xb = b"\xef\xbb\xbf" + XH_TEXT.encode("utf-8")
    eb = open(exe, "rb").read()
    files = [(ZIP_ROOT + EXE_ZIP, eb), (ZIP_ROOT + README, rd), (ZIP_ROOT + XH + XH_README, xb)]
    dirs = [ZIP_ROOT, ZIP_ROOT + XH]
    expected = [ZIP_ROOT, ZIP_ROOT + EXE_ZIP, ZIP_ROOT + README, ZIP_ROOT + XH, ZIP_ROOT + XH + XH_README]
    now = time.localtime()[:6]
    zp = B + "/TarkovHipAimer-v4.1.zip"
    if os.path.exists(zp):
        os.remove(zp)

    def info(name, is_dir):
        zi = UZipInfo(name, date_time=now)
        zi.create_system = 0                   # MS-DOS / Windows
        zi.external_attr = 0x10 if is_dir else 0x20
        zi.compress_type = zipfile.ZIP_STORED if is_dir else zipfile.ZIP_DEFLATED
        return zi

    content = dict(files)
    with zipfile.ZipFile(zp + ".tmp", "w", compresslevel=9) as z:
        for name in expected:
            if name in dirs:
                z.writestr(info(name, True), b"")
            else:
                z.writestr(info(name, False), content[name], compresslevel=9)
    os.replace(zp + ".tmp", zp)

    # --- verify from the raw bytes: entry list, UTF-8 flag in local + central headers, names, CRC, content
    raw = open(zp, "rb").read()
    bad = []
    with zipfile.ZipFile(zp) as z:
        names = z.namelist()
        if names != expected:
            bad.append("entries %s, want %s" % (names, expected))
        if z.testzip() is not None:
            bad.append("CRC error in " + z.testzip())
        for zi in z.infolist():
            if not zi.flag_bits & 0x800:
                bad.append("central header without UTF-8 flag: " + zi.filename)
            o = zi.header_offset
            if raw[o:o + 4] != b"PK\x03\x04":
                bad.append("no local header for " + zi.filename)
                continue
            flag, = struct.unpack_from("<H", raw, o + 6)
            nlen, = struct.unpack_from("<H", raw, o + 26)
            if not flag & 0x800:
                bad.append("local header without UTF-8 flag: " + zi.filename)
            if raw[o + 30:o + 30 + nlen].decode("utf-8") != zi.filename:
                bad.append("local name differs: " + zi.filename)
            if zi.filename in content and z.read(zi) != content[zi.filename]:
                bad.append("content differs: " + zi.filename)
    for nm in names:
        low = nm.lower()
        if low.endswith(("settings.json", ".bat", ".cs", ".pdb", ".log", ".manifest", ".ico", ".config")) or "/obj/" in low or "/bin/" in low:
            bad.append("forbidden entry " + nm)
    rc, out = run(["unzip", "-tq", zp], B + "/unzip-t.log", timeout=120)
    if rc != 0:
        bad.append("Info-ZIP unzip -t: " + out.strip()[-200:])
    if bad:
        fail("6 zip", "; ".join(bad))
        return None
    ok("6 zip", "%d entries exactly as planned; UTF-8 flag 0x800 on all of them (raw local + central headers); CRC ok "
       "(Python + Info-ZIP); %d bytes" % (len(names), os.path.getsize(zp)))

    # --- unzip into staging and scan the extracted tree
    ux = B + "/unzip"
    listed = B + "/unzip.list"             # names the last run extracted (it may have used other names)
    old = open(listed, encoding="utf-8").read().splitlines() if os.path.isfile(listed) else []
    for name in sorted(set(old) | set(expected), key=len, reverse=True):   # files and then their folders, by name
        p = os.path.join(ux, name)
        if name.endswith("/"):
            if os.path.isdir(p) and not os.listdir(p):
                os.rmdir(p)
        elif os.path.isfile(p):
            os.remove(p)
    if os.path.isdir(ux) and any(os.scandir(ux)):
        fail("6 zip", "unexpected files left in %s; remove them by hand" % ux)
        return zp
    with open(listed, "w", encoding="utf-8") as f:
        f.write("\n".join(expected) + "\n")
    with zipfile.ZipFile(zp) as z:
        z.extractall(ux)
    found = []
    for root, _dirs, fs in os.walk(ux):
        for f in fs:
            found.append(os.path.relpath(os.path.join(root, f), ux))
    if sorted(found) != sorted(n for n, _ in files):
        fail("6 zip", "unzipped tree %s" % sorted(found))
    for rel in found:
        p = os.path.join(ux, rel)
        data = open(p, "rb").read()
        if data != content.get(rel):
            fail("6 zip", "unzipped %s differs" % rel)
        scan_words("6 wording", "file name " + rel, rel)
        if rel.endswith(".txt"):
            scan_words("6 wording", rel, data.decode("utf-8-sig"))
        elif rel.endswith(".exe"):
            for w in BANNED_ZH:
                for enc in ("utf-16-le", "utf-8"):
                    if w.encode(enc) in data:
                        fail("6 wording", "%s holds 「%s」 (%s)" % (rel, w, enc))
    if len(problems) == n0:
        ok("6 wording", "unzipped tree (%s): no ban/risk word in names, README texts or exe strings" % ", ".join(sorted(found)))
    return zp


# --------------------------------------------------------------------------------------------- 7 drift + publish
def step_drift(hashes, test_hashes):
    say("7 live tree unchanged since the snapshot?")
    now = live_files()
    report = warn if DRY else fail
    if hashes is not None:
        added, removed = sorted(set(now) - set(hashes)), sorted(set(hashes) - set(now))
        changed = sorted(r for r in now if r in hashes and sha(os.path.join(LIVE, r)) != hashes[r])
        if added or removed or changed:
            report("7 drift", "saved during the run (zip = snapshot, not the live tree): changed %s added %s removed %s"
                   % (changed[:8], added[:5], removed[:5]))
        else:
            ok("7 drift", "src, manifest, icon and README unchanged since the snapshot")
    if test_hashes is not None:
        changed = sorted(r for r, h in test_hashes.items()
                         if not os.path.isfile(os.path.join(DEV, "tests-v4", r)) or sha(os.path.join(DEV, "tests-v4", r)) != h)
        if changed:
            report("7 drift", "tests-v4 files saved during the run: %s" % changed[:8])


def publish(zp, exe, tests_line):
    z_sha = sha(zp)
    if DRY:
        shutil.copyfile(zp, DRY_ZIP + ".tmp")
        os.replace(DRY_ZIP + ".tmp", DRY_ZIP)
        say("  dry run: zip kept at %s; nothing published" % DRY_ZIP)
        return
    for dst in (OUT, SLACK_OUT):
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copyfile(zp, dst + ".tmp")
        os.replace(dst + ".tmp", dst)
        if sha(dst) != z_sha:
            fail("7 publish", "copy differs: " + dst)
    ok("7 publish", "%s and %s (sha256 %s)" % (OUT, SLACK_OUT, z_sha))
    with open(PROGRESS, "a", encoding="utf-8") as f:
        f.write("%s PACKAGE41 package-v41.sh published %s (%d bytes, sha256 %s); exe %d bytes sha256 %s; %s\n"
                % (time.strftime("%H:%M", time.gmtime()), OUT, os.path.getsize(zp), z_sha[:16], os.path.getsize(exe),
                   sha(exe)[:16], tests_line))


# ----------------------------------------------------------------------------------------------------------- main
say("Tarkov Hip Aimer %s package (%s)  %s" % (VERSION, "DRY RUN" if DRY else "FINAL", time.strftime("%Y-%m-%d %H:%M:%S %Z")))
hashes = test_hashes = exe = zp = None
try:
    step_guide()
    hashes = step_snapshot()
    try:
        exe = step_compile()
    except Stop:
        exe = None
    if exe:
        step_resources(exe)
        step_verify(exe)
        step_mono(exe)
    test_hashes = step_tests()
    if exe:
        zp = step_zip(exe)
    else:
        say("6 zip: skipped (no exe)")
except Stop:
    pass
step_drift(hashes, test_hashes)
tests_line = next((f.split(": ", 1)[1] for f in facts if f.startswith("5 tests")), "tests not green")
if not problems and zp:
    publish(zp, exe, tests_line)
say("")
if exe:
    say("exe  %s  %d bytes  sha256 %s  (in the zip as 「%s」)" % (exe, os.path.getsize(exe), sha(exe), EXE_ZIP))
if zp and os.path.isfile(zp):
    say("zip  %s  %d bytes  sha256 %s" % (zp, os.path.getsize(zp), sha(zp)))
say("time %.0f s; logs in %s" % (time.time() - T0, B))
if warnings:
    say("%d warning(s):" % len(warnings))
    for w in warnings:
        say("  - " + w)
if problems:
    say("PACKAGE FAILED (dry run): %d problem(s):" % len(problems))
    for p in problems:
        say("  - " + p)
    sys.exit(1)
say("DRY RUN CLEAN: the final run (no --dry-run) would publish this zip." if DRY else "PACKAGE OK: " + OUT)
PY
