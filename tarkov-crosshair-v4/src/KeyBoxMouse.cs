// KeyBoxMouse.cs -- v5.1.1 (X1, F3): mouse buttons for every key box (按键 page, 丢雷 / 换弹 boxes, setup wizard).
//
// Leo (Windows, 20 Nov): clicking a key box and then pressing a mouse SIDE button did not bind it. A key box only got the
// button through its own MouseDown, which needs the pointer to stay on that box and the press to reach it as a mouse
// message. While a box waits for a key, MouseButtonPoll now also reads the middle / side buttons directly
// (GetAsyncKeyState, every 15 ms, ONLY while waiting -- no timer otherwise) and reports the first new press.
// Left / right buttons stay with the box's own MouseDown, so clicking somewhere else still just cancels.
//
// UI_PURE_CHECK: only the pure edge detector (MouseEdge) is compiled -- for the Linux tests.
using System;

namespace TarkovCrosshair
{
    /// <summary>Pure: which of middle / side 1 / side 2 went from up to down since the last look.</summary>
    internal sealed class MouseEdge
    {
        /// <summary>The buttons watched while a key box waits: middle (4), side 1 (5), side 2 (6).</summary>
        public static readonly int[] Watched = { 0x04, 0x05, 0x06 };

        private readonly bool[] was = new bool[3];

        /// <summary>Remembers what is held right now: a button already down when waiting starts never counts.</summary>
        public void Reset(Func<int, bool> isDown)
        {
            for (int i = 0; i < Watched.Length; i++) was[i] = isDown != null && Safe(isDown, Watched[i]);
        }

        /// <summary>The first watched button that is down now and was up at the last look (0 = none). Updates the state.</summary>
        public int NewPress(Func<int, bool> isDown)
        {
            if (isDown == null) return 0;
            int hit = 0;
            for (int i = 0; i < Watched.Length; i++)
            {
                bool now = Safe(isDown, Watched[i]);
                if (now && !was[i] && hit == 0) hit = Watched[i];
                was[i] = now;
            }
            return hit;
        }

        private static bool Safe(Func<int, bool> isDown, int vk)
        {
            try { return isDown(vk); } catch (Exception) { return false; }
        }
    }
}

#if !UI_PURE_CHECK
namespace TarkovCrosshair
{
    using System.Windows.Forms;

    /// <summary>Polls the middle / side mouse buttons while a key box waits (Start .. Stop). One per window.</summary>
    internal sealed class MouseButtonPoll : IDisposable
    {
        public const int IntervalMs = 15;

        private readonly MouseEdge edge = new MouseEdge();
        private Timer timer;
        private Action<int> onPress;

        public bool Running { get { return timer != null && timer.Enabled; } }

        /// <summary>Starts watching; onPress(vk) runs on the UI thread for the first new press of 4 / 5 / 6, then it stops.</summary>
        public void Start(Action<int> onPress)
        {
            Stop();
            this.onPress = onPress;
            edge.Reset(Native.IsDown);
            if (timer == null)
            {
                timer = new Timer();
                timer.Interval = IntervalMs;
                timer.Tick += OnTick;
            }
            timer.Start();
        }

        public void Stop()
        {
            if (timer != null) timer.Stop();
            onPress = null;
        }

        private void OnTick(object sender, EventArgs e)
        {
            Action<int> a = onPress;
            if (a == null) { Stop(); return; }
            int vk = edge.NewPress(Native.IsDown);
            if (vk == 0) return;
            Stop();
            try { a(vk); } catch (Exception ex) { Program.Log("UI: key box mouse: " + ex.Message); }
        }

        public void Dispose()
        {
            Stop();
            if (timer != null) { timer.Dispose(); timer = null; }
        }
    }
}
#endif
