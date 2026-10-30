using System;

namespace PullThePin.Sim
{
    /// <summary>
    /// Bố cục các màn chơi. Hệ toạ độ: x ∈ [-4.4, 4.4] là lòng màn chơi, khung ở ±4.75,
    /// sàn mặt trên ở y = -7.8. Chốt luôn xuyên ra khung trái/phải để vòng kéo lộ ra ngoài.
    /// </summary>
    public static class Levels
    {
        const float FrameX = 4.75f;

        // {seed, độ khó} đã qua bộ giải: có đáp án, nhiều bẫy, buộc đúng thứ tự (xem Gen.cs)
        static readonly int[,] Table =
        {
            { 99, 1 },
            { 125, 1 },
            { 172, 1 },
            { 214, 1 },
            { 232, 1 },
            { 255, 1 },
            { 275, 1 },
            { 332, 1 },
            { 393, 1 },
            { 457, 1 },
            { 20, 2 },
            { 39, 2 },
            { 43, 2 },
            { 45, 2 },
            { 46, 2 },
            { 50, 2 },
            { 133, 2 },
            { 170, 2 },
            { 171, 2 },
            { 192, 2 },
            { 205, 2 },
            { 230, 2 },
            { 251, 2 },
            { 360, 2 },
            { 382, 2 },
            { 426, 2 },
            { 438, 2 },
            { 442, 2 },
            { 13, 3 },
            { 31, 3 },
            { 59, 3 },
            { 60, 3 },
            { 66, 3 },
            { 80, 3 },
            { 83, 3 },
            { 84, 3 },
            { 92, 3 },
            { 95, 3 },
            { 102, 3 },
            { 134, 3 },
            { 141, 3 },
            { 161, 3 },
            { 174, 3 },
            { 185, 3 },
            { 193, 3 },
            { 198, 3 },
            { 208, 3 },
            { 260, 3 },
            { 298, 3 },
            { 315, 3 },
            { 343, 3 },
            { 355, 3 },
            { 363, 3 },
            { 377, 3 },
            { 380, 3 },
            { 405, 3 },
            { 527, 3 },
            { 528, 3 },
            { 534, 3 },
            { 539, 3 },
            { 8, 4 },
            { 43, 4 },
            { 52, 4 },
            { 57, 4 },
            { 86, 4 },
            { 107, 4 },
            { 140, 4 },
            { 156, 4 },
            { 231, 4 },
            { 238, 4 },
            { 243, 4 },
            { 255, 4 },
            { 321, 4 },
            { 330, 4 },
            { 334, 4 },
            { 342, 4 },
            { 352, 4 },
            { 369, 4 },
            { 372, 4 },
            { 399, 4 },
            { 428, 4 },
            { 479, 4 },
            { 499, 4 },
            { 574, 4 },
            { 594, 4 }
        };

        public static int Count => 1 + Table.GetLength(0);

        /// <summary>Số màn hiển thị trong danh sách chọn màn (bản gốc + bản lật gương).</summary>
        public static int Total => Count * 2;

        public static World Build(int index)
        {
            int n = Count;
            int i = index % n;
            bool flip = (index / n) % 2 == 1;
            World w;
            if (i == 0)
            {
                w = new World();
                w.AddFrame();
                L1(w);
                if (flip) w.MirrorX();
                w.Begin();
                return w;
            }
            w = Gen.Build(Table[i - 1, 0], Table[i - 1, 1], false);
            if (flip) w.MirrorX();
            w.Begin();
            return w;
        }

        // chốt thò ra khung phải: tip -> khung
        static void PinR(World w, float tipX, float tipY, float tiltDeg = 0f)
            => w.AddPin(tipX, tipY, tiltDeg, (FrameX - tipX) / (float)Math.Cos(tiltDeg * Math.PI / 180.0));

        // chốt thò ra khung trái
        static void PinL(World w, float tipX, float tipY, float tiltDeg = 0f)
            => w.AddPin(tipX, tipY, 180f - tiltDeg, (tipX + FrameX) / (float)Math.Cos(tiltDeg * Math.PI / 180.0));

        // phễu hai mái dốc dẫn xuống giữa, khe rộng gap
        static void Funnel(World w, float yTop, float yBottom, float gap, float thick = 0.35f)
        {
            float x0 = 4.4f, x1 = gap * 0.5f;
            float dx = x0 - x1, dy = yTop - yBottom;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            float ang = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            float cx = (x0 + x1) * 0.5f, cy = (yTop + yBottom) * 0.5f;
            w.AddWall(-cx, cy, len, thick, -ang);
            w.AddWall(cx, cy, len, thick, ang);
        }

        // Màn 1: một chốt, một phễu — làm quen thao tác
        static void L1(World w)
        {
            w.AddWall(-3.6f, 3.7f, 0.4f, 4.4f);
            w.AddWall(3.6f, 3.7f, 0.4f, 4.4f);
            w.AddWall(0f, 6.0f, 7.6f, 0.4f);
            PinR(w, -3.6f, 1.9f);
            w.AddBalls(0f, 2.4f, 5, 2, 0);
            Funnel(w, 0.6f, -2.0f, 1.2f);
            w.SetJar(0f, -7.5f, 2.4f, 2.4f, 0);
        }

    }
}
