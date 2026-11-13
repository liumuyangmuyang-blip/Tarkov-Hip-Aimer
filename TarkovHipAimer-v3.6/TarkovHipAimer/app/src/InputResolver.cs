// PURE file: only System. Compiles under net48 (C# 5) and net8.0.
using System;

namespace TarkovCrosshair
{
    /// <summary>
    /// v2.4.1: opposite movement keys — the last pressed key wins (Leo, 12 Oct 2026: "按住a不放再按d，人物会往右走").
    /// v2.4.2: Leo checked in game (12 Oct 2026): releasing the newer key while the older one is still held goes back to
    /// the older key ("松开d a还按着会往左边走"), and W/S works the same ("按住w再按s会往后走", "ws和ad在键盘上是后输入优先").
    /// Per pair (A/D; W/S when ApplyToFwdBack) each key remembers the poll index of its latest press edge.
    /// A held key is superseded when the opposite key was pressed after it (with FallbackToHeld: only while that key is
    /// still held); the active key = the held key not superseded.
    /// Pressed in the same poll = neither superseded = both cancel (centre), like v2.4.
    /// Lean, fire, ADS, free look and hand switch pass through unchanged. Applied to LIVE input only.
    /// </summary>
    public sealed class InputResolver
    {
        /// <summary>true (default, confirmed in game) = releasing the newer key falls back to the still-held older key.
        /// false = v2.4.1's literal rule: the axis stays centred until the older key is pressed again.</summary>
        public static bool FallbackToHeld = true;
        /// <summary>W/S uses the same rule (confirmed in game, v2.4.2). false = W/S exactly as v2.4.</summary>
        public static bool ApplyToFwdBack = true;

        // [0] Left (A), [1] Right (D), [2] Back (S), [3] Fwd (W)
        private readonly bool[] down = new bool[4];
        private readonly long[] pressed = new long[4];
        private long poll;

        public InputResolver() { Reset(); }

        /// <summary>Forget all key history: keys still held count as freshly pressed on the next poll.</summary>
        public void Reset()
        {
            for (int i = 0; i < 4; i++) { down[i] = false; pressed[i] = 0; }
            poll = 0;
        }

        /// <summary>Call once per poll with the physical key state; returns the state the engine should see.</summary>
        public InputState Resolve(InputState raw)
        {
            poll++;
            bool[] now = { raw.Left, raw.Right, raw.Back, raw.Fwd };
            for (int i = 0; i < 4; i++)
            {
                if (now[i] && !down[i]) pressed[i] = poll;
                down[i] = now[i];
            }
            InputState r = raw;
            bool a, b;
            Pair(0, 1, out a, out b);
            r.Left = a; r.Right = b;
            if (ApplyToFwdBack)
            {
                Pair(2, 3, out a, out b);
                r.Back = a; r.Fwd = b;
            }
            return r;
        }

        private void Pair(int i, int j, out bool ai, out bool aj)
        {
            ai = down[i] && !Superseded(i, j);
            aj = down[j] && !Superseded(j, i);
            if (ai && aj) { ai = false; aj = false; } // same-poll press: cancel (centre)
        }

        /// <summary>Key k is superseded by its opposite o when o was pressed after k: while o is still held (FallbackToHeld,
        /// the default), or for good until k is pressed again (FallbackToHeld = false, v2.4.1).</summary>
        private bool Superseded(int k, int o)
        {
            if (pressed[o] <= pressed[k]) return false;
            return FallbackToHeld ? down[o] : true;
        }
    }

    /// <summary>
    /// Live-input glue used by the render thread: resolves opposite keys for LIVE input only and resets the resolver
    /// whenever the ring is switched on and whenever the preview returns to 实时. Previews get the raw state untouched.
    /// </summary>
    public sealed class LiveInputFilter
    {
        public readonly InputResolver Resolver = new InputResolver();
        private bool prevOn, prevLive;

        public InputState Next(InputState raw, bool ringOn, bool live)
        {
            if ((ringOn && !prevOn) || (live && !prevLive)) Resolver.Reset();
            prevOn = ringOn;
            prevLive = live;
            return live ? Resolver.Resolve(raw) : raw;
        }
    }
}
