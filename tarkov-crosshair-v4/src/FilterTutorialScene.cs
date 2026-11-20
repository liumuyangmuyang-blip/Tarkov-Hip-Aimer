// FilterTutorialScene.cs -- 滤镜教程 (v4.2, W3): the procedurally drawn dark room used as the preview picture.
// A Tarkov-like interior: dark left corner with a person standing in the shadow, bright window with sky and sun on the
// right, coloured crates / barrel / tarp in the middle. Drawn with System.Drawing primitives only (no screenshot, no
// image files). The corner wall and the person use the exact levels the model's goals are computed on.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TarkovCrosshair
{
    public static class FilterTutorialScene
    {
        private static Color Grey(int v) { return Color.FromArgb(255, v, v, v); }

        /// <summary>Draws the scene into a w x h bitmap graphics. enemy / window = their boxes as fractions of w, h.</summary>
        public static void Draw(Graphics g, int w, int h, out RectangleF enemy, out RectangleF window)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float W = w, H = h;
            int c = FilterTutorialModel.CornerLevel, e = FilterTutorialModel.EnemyLevel;

            // back wall + ceiling + floor
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(0, 0, W, H * 0.7f), Grey(30), Grey(42), 90f))
                g.FillRectangle(b, 0, 0, W, H * 0.7f);
            using (SolidBrush b = new SolidBrush(Grey(22))) g.FillRectangle(b, 0, 0, W, H * 0.09f);
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(0, H * 0.7f, W, H * 0.3f + 1), Color.FromArgb(255, 40, 37, 32), Color.FromArgb(255, 26, 24, 21), 90f))
                g.FillRectangle(b, 0, H * 0.7f, W, H * 0.3f + 1);
            using (Pen p = new Pen(Color.FromArgb(255, 33, 31, 27), Math.Max(1f, H / 200f)))
                for (int i = 1; i < 6; i++) { float y = H * 0.7f + H * 0.3f * i * i / 36f; g.DrawLine(p, 0, y, W, y); }
            // wall panels
            using (Pen p = new Pen(Grey(34), Math.Max(1f, W / 400f)))
                for (int i = 0; i < 8; i++) { float x = W * (0.38f + 0.08f * i); g.DrawLine(p, x, H * 0.09f, x, H * 0.7f); }

            // window (right) with sky, sun and buildings outside
            RectangleF win = new RectangleF(W * 0.63f, H * 0.14f, W * 0.3f, H * 0.42f);
            using (LinearGradientBrush b = new LinearGradientBrush(win, Color.FromArgb(255, 226, 236, 248), Color.FromArgb(255, 246, 248, 250), 90f))
                g.FillRectangle(b, win);
            int sky = FilterTutorialModel.SkyLevel;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, sky, sky, sky))) g.FillRectangle(b, win.X, win.Y + win.Height * 0.35f, win.Width, win.Height * 0.25f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 255, 252, 236)))
                g.FillEllipse(b, win.X + win.Width * 0.62f, win.Y + win.Height * 0.08f, win.Height * 0.22f, win.Height * 0.22f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 200, 206, 212)))
            {
                g.FillRectangle(b, win.X, win.Y + win.Height * 0.62f, win.Width * 0.28f, win.Height * 0.38f);
                g.FillRectangle(b, win.X + win.Width * 0.34f, win.Y + win.Height * 0.52f, win.Width * 0.22f, win.Height * 0.48f);
                g.FillRectangle(b, win.X + win.Width * 0.62f, win.Y + win.Height * 0.70f, win.Width * 0.38f, win.Height * 0.30f);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 176, 184, 190)))
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
                    g.FillRectangle(b, win.X + win.Width * (0.05f + 0.07f * i), win.Y + win.Height * (0.68f + 0.09f * j), win.Width * 0.03f, win.Height * 0.04f);
            using (Pen p = new Pen(Grey(44), Math.Max(2f, W / 140f)))
            {
                g.DrawRectangle(p, win.X, win.Y, win.Width, win.Height);
                g.DrawLine(p, win.X + win.Width / 2, win.Y, win.X + win.Width / 2, win.Bottom);
                g.DrawLine(p, win.X, win.Y + win.Height / 2, win.Right, win.Y + win.Height / 2);
            }
            // light patch on the floor below the window
            PointF[] patch = { new PointF(win.X, H * 0.74f), new PointF(win.Right, H * 0.74f), new PointF(W * 0.98f, H), new PointF(W * 0.55f, H) };
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 74, 70, 60))) g.FillPolygon(b, patch);

            // coloured props in the middle (dark-ish so the saturation step shows on them)
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 46, 72, 40))) g.FillRectangle(b, W * 0.40f, H * 0.6f, W * 0.13f, H * 0.2f);
            using (Pen p = new Pen(Color.FromArgb(255, 36, 56, 32), Math.Max(1f, W / 300f)))
            { g.DrawRectangle(p, W * 0.40f, H * 0.6f, W * 0.13f, H * 0.2f); g.DrawLine(p, W * 0.40f, H * 0.6f, W * 0.53f, H * 0.8f); }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 44, 64, 112))) g.FillRectangle(b, W * 0.44f, H * 0.47f, W * 0.08f, H * 0.13f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 112, 36, 30))) g.FillRectangle(b, W * 0.555f, H * 0.56f, W * 0.055f, H * 0.26f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 88, 28, 24)))
            { g.FillRectangle(b, W * 0.555f, H * 0.62f, W * 0.055f, H * 0.02f); g.FillRectangle(b, W * 0.555f, H * 0.73f, W * 0.055f, H * 0.02f); }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 150, 126, 34))) g.FillRectangle(b, W * 0.47f, H * 0.28f, W * 0.06f, H * 0.08f);

            // dark corner (left) and doorway
            using (SolidBrush b = new SolidBrush(Grey(c))) g.FillRectangle(b, 0, 0, W * 0.36f, H);
            using (Pen p = new Pen(Grey(c + 3), Math.Max(1f, W / 260f)))
            { g.DrawRectangle(p, W * 0.06f, H * 0.18f, W * 0.24f, H * 0.82f); }

            // the person in the shadow: helmet, head, torso with plate carrier, legs, rifle
            float px = W * 0.17f, top = H * 0.30f, u = H / 100f;
            using (SolidBrush b = new SolidBrush(Grey(e)))
            {
                g.FillEllipse(b, px - 3.2f * u, top, 6.4f * u, 7f * u);                    // head + helmet
                g.FillRectangle(b, px - 4.2f * u, top + 6.5f * u, 8.4f * u, 21f * u);      // torso
                g.FillRectangle(b, px - 6f * u, top + 8f * u, 2.5f * u, 15f * u);          // left arm
                g.FillRectangle(b, px + 3.6f * u, top + 8f * u, 2.5f * u, 13f * u);        // right arm
                g.FillRectangle(b, px - 3.8f * u, top + 27f * u, 3.2f * u, 25f * u);       // legs
                g.FillRectangle(b, px + 0.6f * u, top + 27f * u, 3.2f * u, 25f * u);
                g.FillRectangle(b, px + 2f * u, top + 14f * u, 13f * u, 1.8f * u);         // rifle
            }
            using (SolidBrush b = new SolidBrush(Grey(e + 2)))
                g.FillRectangle(b, px - 3.4f * u, top + 9f * u, 6.8f * u, 9f * u);         // plate carrier
            enemy = new RectangleF((px - 6.5f * u) / W, top / H, (21.5f * u) / W, 52.5f * u / H);
            window = new RectangleF(win.X / W, win.Y / H, win.Width / W, win.Height / H);
        }
    }
}
