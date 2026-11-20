# Windows CI for Tarkov Hip Aimer: setup and use

What it does: every time you push the code to your private GitHub repo, a Windows machine at GitHub:

1. builds `准星 TarkovHipAimer.exe` and checks it has 0 errors and 0 warnings,
2. runs all tests (`tests-v4` and the filter-tutorial tests),
3. starts the real exe 3 times and records how long startup and opening the settings window take,
4. opens every settings page, tutorial step, dialog and window at 100 %, 150 % and 200 % Windows display scaling on real
   Windows fonts, checks that every text fits its box and nothing overlaps, and takes screenshots.

You get a green ✅ or red ❌ per run, a one-page summary, and a zip with screenshots and logs.

All commands below run **on the Cloud Desktop** (SSH into it first), not in PowerShell on your laptop.

## Before you start (please check)

  before step 3.
- **Cost.** A private repo on the free GitHub plan includes $12 of Actions usage per month. Windows machines cost
  $0.010 per minute, so that's about 1,200 Windows minutes a month. Artifact storage is 500 MB
  ([GitHub billing docs](https://docs.github.com/en/billing/concepts/product-billing/github-actions)).
  I could not time a real run. My estimate is 40-60 minutes per run (UNVERIFIED), which is roughly 20 runs a month.
  Without a payment method on file, GitHub stops the runs when the $12 is used up. It does not charge you.
- Screenshots and results are kept for 7 days, then deleted automatically.

## Step 1: create the private repo (once, in your browser)

1. Go to github.com and sign in.
2. Top right **+** → **New repository**.
3. Name: `tarkov-ci` (any name works). Select **Private**. Leave "Add a README" **unticked**.
4. Click **Create repository**. Keep the page open. You need the address it shows in step 3.

## Step 2: let the Cloud Desktop sign in to GitHub (once)

Use option A. If A fails with "Connection timed out", use option B. I could not test from here which one the Cloud
Desktop network allows.

**Option A: SSH key (no password to type later)**

1. Run: `ssh-keygen -t ed25519 -C "tarkov-ci" -f ~/.ssh/github_tarkov -N ""`
2. Run: `cat ~/.ssh/github_tarkov.pub` and copy the line it prints.
3. github.com → your picture (top right) → **Settings** → **SSH and GPG keys** → **New SSH key** → paste → **Add SSH key**.
4. Run: `printf 'Host github.com\n  IdentityFile ~/.ssh/github_tarkov\n' >> ~/.ssh/config`
5. Run: `ssh -T git@github.com`. Type `yes` if it asks. "Hi <name>! You've successfully authenticated" means it worked.
6. Your repo address is `git@github.com:<your-github-name>/tarkov-ci.git`.

**Option B: access token (HTTPS)**

1. github.com → your picture → **Settings** → **Developer settings** → **Personal access tokens** → **Fine-grained tokens**
   → **Generate new token**.
2. Expiration: 90 days. Repository access: **Only select repositories** → `tarkov-ci`.
3. Permissions → Repository: **Contents: Read and write** and **Workflows: Read and write**. Click **Generate token** and copy it.
4. Your repo address is `https://github.com/<your-github-name>/tarkov-ci.git`. When the push asks, the username is your
   GitHub name and the password is the token.

## Step 3: first push

Run:

```
bash /home/dev/tarkov-crosshair-dev/ci/push-to-github.sh <your repo address from step 2>
```

The script copies only what the CI needs into `~/tarkov-ci-repo` (app source, filter module source, tests, CI scripts,
about 8 MB). It stops without pushing if anything looks like a key or token, or if network/worker files would go in.
To see what would be pushed without pushing, run it with `--dry-run`.

## Step 4: see the results

1. Open your repo on github.com → **Actions** tab → **Windows CI** → the newest run (top of the list).
2. A yellow dot means it is still running. ✅ means everything passed. ❌ means something failed.
3. Scroll down the run page to the **summary**. It has one row per check, with the detail:
   - **Build**: errors / warnings.
   - **Tests**: pass / fail counts, plus the failing test lines.
   - **Startup and settings timing**: ms until 启动完成 for each start, and ms until the settings window appeared.
   - **Layout probe**: one row per screen size and scale, the number of checks passed and failed, and the first failures.
     **Not run** lists combinations this Windows machine could not switch to. Windows only allows high scaling on
     larger screens, and GitHub's machine may not offer every resolution. A combination that did not run is never
     counted as passed. The run goes red only if a whole scale (100, 150 or 200 %) never ran.
4. At the bottom under **Artifacts**, click `tarkov-ci-<number>` to download a zip with:
   - `SUMMARY.md`: the same summary.
   - `shots/`: screenshots. The name says the screen and scale, e.g. `1920x1080@150pct-settings-basic-default-03-general.png`.
     `startup-run1.png` / `settings-run1.png` are the desktop after startup and after the settings window opened.
   - `probe-fails.txt`: every layout failure. `probe-times.txt`: page open times at each scale.
   - `timing.txt`: all of the app's 启动耗时 lines. `tests-v4.log`, `build-exe.log`, and so on: full logs.
   - `app/TarkovHipAimer/`: the exe that was tested.

## Later

- **After changing the code:** run the same command without the address:
  `bash /home/dev/tarkov-crosshair-dev/ci/push-to-github.sh`
- **Run again without pushing, or a cheaper run:** Actions → **Windows CI** → **Run workflow** (right side). You can
  narrow the screen sizes or scales (e.g. scales `150`) and pick screenshots `none` / `pages` / `all`.
- **Stop it:** repo **Settings** → **Actions** → **General** → **Disable actions**. Or delete the repo. Nothing else
  needs undoing. The local copy `~/tarkov-ci-repo` can be deleted any time.

## If something goes wrong

- `Permission denied (publickey)`: step 2A, item 4 or 5, was not done.
- `Connection timed out` on push: use option B.
- The run is red at **Build** but builds fine locally: download the artifact and open `build-exe.log`.
- **Timing** red with "exited with code ...": the exe would not start on GitHub's machine. Check `timing.txt` and the
  screenshots.
- **Probe** red with "Windows did not switch to the requested display scale": GitHub's machine refused the scale. The
  results are then labelled with the real scale. Tell Kiro.

## For Kiro / developers

- Files: `windows-ci.yml` (copied to `.github/workflows/`), `build.ps1`, `tests.ps1`, `timing.ps1`, `probe.ps1`,
  `summary.ps1`, `common.ps1`, `probe/UiCProbe.cs` (Windows copy of the V501-UI-C probe, plus modes `timing` and
  `windows`), `tools/DpiTool.cs` (resolution + display scale), `push-to-github.sh`, `local-check.sh`.
- Real DPI, not simulated: DpiTool sets the scale through DisplayConfig (the Settings app's own call) and each probe
  mode runs in a new process, which gets the new system DPI (Windows 10 1803+ per-process system DPI,
  `EnablePerProcessSystemDPI=1`). The probe fails when its DPI is not the requested one.
- The tests use the Cloud Desktop's absolute paths. `tests.ps1` links `<drive>:\local\home\dev\...` and
  `<drive>:\home\dev` to the checkout with junctions. `.gitattributes` turns off CRLF conversion.
- Local check before pushing: `PWSH=... ACTIONLINT=... bash /home/dev/tarkov-crosshair-dev/ci/local-check.sh`.
