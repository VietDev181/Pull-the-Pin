using System;

namespace PullThePin.Sim
{
    /// <summary>
    /// Bộ sinh màn: các "ngăn" xếp tầng (mỗi ngăn một chốt sàn), kết thúc bằng phễu/bình hoặc cầu qua hố bẫy.
    /// Bẫy: dung nham, gai, cưa quay, hàng rào điện chớp theo nhịp, bom và bóng gai treo trên chốt, cọc đẩy bóng.
    /// Chỉ các seed đã qua bộ giải (có đáp án, nhiều lối thua) mới được đưa vào Levels.
    /// </summary>
    public static class Gen
    {
        const float FrameX = 4.75f;

        static void PinR(World w, float tipX, float tipY, float tilt = 0f)
            => w.AddPin(tipX, tipY, tilt, (FrameX - tipX) / (float)Math.Cos(tilt * Math.PI / 180.0));

        static void PinL(World w, float tipX, float tipY, float tilt = 0f)
            => w.AddPin(tipX, tipY, 180f - tilt, (tipX + FrameX) / (float)Math.Cos(tilt * Math.PI / 180.0));

        static void Funnel(World w, float yTop, float yBottom, float gap)
        {
            float x0 = 4.4f, x1 = gap * 0.5f;
            float dx = x0 - x1, dy = yTop - yBottom;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            float ang = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            float cx = (x0 + x1) * 0.5f, cy = (yTop + yBottom) * 0.5f;
            w.AddWall(-cx, cy, len, 0.35f, -ang);
            w.AddWall(cx, cy, len, 0.35f, ang);
        }

        /// <summary>Hố bẫy: 0 dung nham, 1 gai, 2 máy nghiền (hàng cưa quay phía trên + gai bên dưới).</summary>
        static void Pit(World w, int kind, float cx, float cy, float wd, float ht)
        {
            if (kind == 0) { w.AddLava(cx, cy, wd, ht); return; }
            if (kind == 1) { w.AddSpikes(cx, cy, wd, ht); return; }
            float top = cy + ht * 0.5f;
            w.AddSpikes(cx, cy - ht * 0.18f, wd, ht * 0.64f);
            int n = Math.Max(1, (int)(wd / 1.25f));
            float step = wd / n;
            for (int i = 0; i < n; i++)
                w.AddSaw(cx - wd * 0.5f + step * (i + 0.5f), top - 0.25f, 0.55f);
        }

        /// <param name="d">độ khó 1..4</param>
        public static World Build(int seed, int d, bool begin = true)
        {
            var r = new Random(seed * 7919 + d * 104729 + 17);
            var w = new World();
            w.AddFrame();

            int stages = d >= 4 ? 4 : d >= 3 ? 3 : 2;
            int finish = r.Next(3);                      // 0 phễu, 1 cầu qua hố bẫy, 2 phễu + hố bẫy hai bên
            if (stages == 4 && finish == 1) finish = r.Next(2) * 2;
            bool bridge = finish == 1;
            int pit = r.Next(3);
            bool color = r.NextDouble() < 0.35 + 0.15 * d;
            bool pocket = r.NextDouble() < 0.25 + 0.12 * d;
            bool spikeBall = pocket && r.Next(2) == 0;
            bool zap = d >= 3 && r.NextDouble() < 0.45;
            int jarColor = color ? 1 + r.Next(3) : 0;
            int ps = r.Next(2) == 0 ? -1 : 1;
            int chamberSide = pocket ? -ps : 0;
            if (bridge) { chamberSide = -1; ps = 1; }

            float[] floors = stages == 4 ? new[] { 5.0f, 3.0f, 1.0f, -1.0f } : new[] { 4.6f, 2.3f, 0.0f };
            float wallH = stages == 4 ? 1.6f : 2.1f;
            int rowsMax = stages == 4 ? 2 : 3;
            int colorStage = r.Next(stages);
            for (int k = 0; k < stages; k++)
            {
                int side = chamberSide != 0 ? chamberSide : (r.Next(2) == 0 ? -1 : 1);
                float width;
                if (bridge) width = 2.4f + (float)r.NextDouble() * 2.2f;
                else if (pocket) width = 2.6f + (float)r.NextDouble() * 4.0f;
                else width = 2.6f + (float)r.NextDouble() * 3.4f;
                float e = side * 4.4f - side * width;
                float f = floors[k];

                w.AddWall(e, f - 0.4f + wallH * 0.5f, 0.4f, wallH);
                if (k == 0) w.AddWall((side * 4.4f + e) * 0.5f, f - 0.4f + wallH + 0.2f, Math.Abs(side * 4.4f - e), 0.3f);
                if (side < 0) PinL(w, e, f); else PinR(w, e, f);

                int c = (color && k == colorStage) ? jarColor : 0;
                int n = c > 0 ? 3 + r.Next(2) : 5 + r.Next(4 + d);
                float inner = width - 0.5f;
                int cols = Math.Max(1, Math.Min(n, (int)(inner / 0.46f)));
                int rows = Math.Min(rowsMax, (n + cols - 1) / cols);
                w.AddBalls((side * 4.4f + e) * 0.5f - side * 0.1f, f + 0.4f, cols, rows, c);
            }

            if (pocket)
            {
                float fp = floors[0];
                w.AddWall(ps * 3.05f, fp + 0.65f, 0.3f, 2.1f);
                w.AddWall(ps * 3.75f, fp + 1.9f, 1.6f, 0.3f);
                if (ps > 0) PinR(w, 3.0f, fp); else PinL(w, -3.0f, fp);
                if (spikeBall) w.AddSpikeBall(ps * 3.75f, fp + 0.5f); else w.AddBomb(ps * 3.75f, fp + 0.5f);
            }

            int defl = d >= 2 ? r.Next(3) : 0;
            float lastFloor = floors[stages - 1];
            float zapY = 0f, zapX = 0f, zapW = 0f;
            if (bridge)
            {
                float tilt = 6f + (float)r.NextDouble() * 5f;
                PinL(w, 1.6f, -2.3f, tilt);
                Pit(w, pit, -1.4f, -6.2f, 6.6f, 3.0f);
                float jw = 1.8f + (float)r.NextDouble() * 0.5f;
                w.SetJar(3.3f, -7.5f, jw, 2.6f, jarColor);
                zapX = 3.3f; zapY = -3.9f; zapW = jw + 0.5f;
            }
            else
            {
                float yTop = lastFloor - 2.0f;
                float fh = stages == 4 ? 1.6f : 2.4f;
                float gap = 1.0f + (float)r.NextDouble() * 0.7f;
                Funnel(w, yTop, yTop - fh, gap);
                float jw = 2.0f + (float)r.NextDouble() * 0.9f;
                w.SetJar(0f, -7.5f, jw, 2.4f, jarColor);
                zapX = 0f; zapY = yTop - fh - 0.5f; zapW = gap + 0.3f;
                if (finish == 2)
                {
                    float lx = jw * 0.5f + 0.9f;
                    float lw = 4.4f - lx;
                    Pit(w, pit, -(lx + lw * 0.5f), -7.0f, lw, 1.6f);
                    Pit(w, pit, lx + lw * 0.5f, -7.0f, lw, 1.6f);
                }
                if (d >= 2 && r.NextDouble() < 0.55)
                {
                    int np = 3 + r.Next(4);
                    for (int i = 0; i < np; i++)
                        w.AddPeg(-3.0f + (float)r.NextDouble() * 6.0f, yTop + 0.3f + (float)r.NextDouble() * 0.9f);
                }
            }
            if (zap)
                w.AddZap(zapX, zapY, zapW, 0.22f, 0f, 4.0f, 1.1f, (float)r.NextDouble() * 4.0f);

            for (int i = 0; i < defl; i++)
            {
                float sgn = r.Next(2) == 0 ? -1f : 1f;
                float dx = sgn * (1.2f + (float)r.NextDouble() * 2.6f);
                float dy = lastFloor - 0.8f - (float)r.NextDouble() * 0.2f;
                if (bridge) { dx = -Math.Abs(dx); dy = lastFloor - 0.8f; }
                float ang = sgn * (-(14f + (float)r.NextDouble() * 18f));
                w.AddWall(dx, dy, 1.6f + (float)r.NextDouble() * 1.0f, 0.3f, ang);
            }

            if (r.Next(2) == 0) w.MirrorX();
            if (begin) w.Begin();
            return w;
        }
    }
}
