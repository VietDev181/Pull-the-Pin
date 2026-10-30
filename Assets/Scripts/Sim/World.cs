using System;
using System.Collections.Generic;

namespace PullThePin.Sim
{
    public struct V2
    {
        public float x, y;
        public V2(float x, float y) { this.x = x; this.y = y; }

        public static V2 operator +(V2 a, V2 b) => new V2(a.x + b.x, a.y + b.y);
        public static V2 operator -(V2 a, V2 b) => new V2(a.x - b.x, a.y - b.y);
        public static V2 operator *(V2 a, float s) => new V2(a.x * s, a.y * s);
        public static V2 operator *(float s, V2 a) => new V2(a.x * s, a.y * s);

        public float Len => (float)Math.Sqrt(x * x + y * y);
        public float Sqr => x * x + y * y;
        public static float Dot(V2 a, V2 b) => a.x * b.x + a.y * b.y;
    }

    /// <summary>Hộp có hướng (tường, bình, dung nham, thân chốt).</summary>
    public sealed class Box
    {
        public V2 c;
        public V2 h;
        public float ang;   // radian
        float cs, sn;

        public Box(float cx, float cy, float w, float hgt, float deg)
        {
            c = new V2(cx, cy);
            h = new V2(w * 0.5f, hgt * 0.5f);
            SetAngle(deg * (float)Math.PI / 180f);
        }

        public void SetAngle(float rad)
        {
            ang = rad;
            cs = (float)Math.Cos(rad);
            sn = (float)Math.Sin(rad);
        }

        public float AngleDeg => ang * 180f / (float)Math.PI;

        public V2 ToLocal(V2 p)
        {
            float dx = p.x - c.x, dy = p.y - c.y;
            return new V2(dx * cs + dy * sn, -dx * sn + dy * cs);
        }

        public V2 DirToWorld(V2 l) => new V2(l.x * cs - l.y * sn, l.x * sn + l.y * cs);

        public float Sd(V2 p)
        {
            V2 l = ToLocal(p);
            float dx = Math.Abs(l.x) - h.x, dy = Math.Abs(l.y) - h.y;
            float ox = Math.Max(dx, 0f), oy = Math.Max(dy, 0f);
            return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(dx, dy), 0f);
        }
    }

    public enum PinState { Idle, Pulling, Gone }

    /// <summary>
    /// Chốt: thanh kim loại, đầu "tip" nằm trong lòng màn chơi, thân xuyên qua tường,
    /// vòng kéo ở đầu ngoài. Kéo = trượt dọc theo dir cho tới khi rời hẳn.
    /// </summary>
    public sealed class Pin
    {
        public const float HalfThick = 0.1f;
        public const float RingRadius = 0.3f;
        const float Accel = 110f;
        const float MaxSpeed = 15f;

        public V2 tip, dir;
        public float length;
        public PinState state = PinState.Idle;
        public float offset, speed;
        public readonly Box box;

        public Pin(float tipX, float tipY, float deg, float len)
        {
            float a = deg * (float)Math.PI / 180f;
            dir = new V2((float)Math.Cos(a), (float)Math.Sin(a));
            tip = new V2(tipX, tipY);
            length = len;
            box = new Box(0, 0, len, HalfThick * 2f, deg);
            Sync();
        }

        public V2 Velocity => state == PinState.Pulling ? dir * speed : new V2(0, 0);
        public V2 Center => tip + dir * (length * 0.5f + offset);
        public V2 RingCenter => tip + dir * (length + RingRadius + offset);

        void Sync() { box.c = Center; }

        public void Pull()
        {
            if (state == PinState.Idle) { state = PinState.Pulling; speed = 0f; }
        }

        public void Update(float dt)
        {
            if (state != PinState.Pulling) return;
            speed = Math.Min(MaxSpeed, speed + Accel * dt);
            offset += speed * dt;
            Sync();
            if (offset > length + 1.5f) state = PinState.Gone;
        }

        public float DistanceTo(V2 p)
        {
            // khoảng cách tới đoạn thân + vòng kéo (dùng cho chạm chọn chốt)
            V2 a = tip + dir * offset;
            V2 b = tip + dir * (length + RingRadius * 2f + offset);
            V2 ab = b - a, ap = p - a;
            float t = Math.Max(0f, Math.Min(1f, V2.Dot(ap, ab) / ab.Sqr));
            return (p - (a + ab * t)).Len;
        }
    }

    public enum BodyKind { Ball, Bomb, Spike }

    public sealed class Body
    {
        public V2 p, v;
        public float r, invM, rot, flash, jarTime;
        public int color;           // 0 = trắng, >0 = màu
        public BodyKind kind;
        public bool alive = true, inJar;
    }

    public sealed class Jar
    {
        public float cx, floorY, w, h;
        public int color;           // 0 = nhận mọi màu
        public Box back;
        public readonly List<Box> glass = new List<Box>();

        public bool Contains(V2 p) =>
            Math.Abs(p.x - cx) < w * 0.5f - 0.02f && p.y > floorY && p.y < floorY + h;
    }

    public enum HazardKind { Spikes, Saw, Zap }

    /// <summary>Bẫy tĩnh/động: gai, cưa quay (có thể tuần tra), hàng rào điện chớp theo nhịp.</summary>
    public sealed class Hazard
    {
        public HazardKind kind;
        public Box box;                 // Spikes, Zap
        public V2 c;                    // Saw (vị trí hiện tại)
        public float r, rot;
        public V2 a, b;                 // Saw tuần tra a <-> b
        public float speed, phase;
        public float period = 4f, on = 1f, offset;   // Zap

        public bool Patrols => kind == HazardKind.Saw && (a.x != b.x || a.y != b.y);

        public bool Active(float t) => kind != HazardKind.Zap || ((t + offset) % period) < on;
        /// <summary>0 tắt, 1 sắp bật (cảnh báo), 2 đang bật.</summary>
        public int ZapState(float t)
        {
            float m = (t + offset) % period;
            if (m < on) return 2;
            return m > period - 0.7f ? 1 : 0;
        }

        public void Update(float t, float dt)
        {
            if (kind != HazardKind.Saw) return;
            rot += dt * 7f;
            if (Patrols)
            {
                float k = 0.5f + 0.5f * (float)Math.Sin(t * speed + phase);
                c = new V2(a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k);
            }
        }

        public bool Kills(V2 p, float br, float t)
        {
            switch (kind)
            {
                case HazardKind.Saw: return (p - c).Len < r + br * 0.5f;
                case HazardKind.Spikes: return box.Sd(p) < br * 0.55f;
                default: return Active(t) && box.Sd(p) < br * 0.6f;
            }
        }
    }

    public sealed class Peg { public V2 c; public float r; }

    public enum GameState { Playing, Won, Lost }
    public enum LoseReason { None, Lava, Bomb, WrongColor, Stuck, Hazard }
    public enum EventKind { PinPulled, BallInJar, Converted, Burn, Explode, Won, Lost, Hurt }

    public struct SimEvent
    {
        public EventKind kind;
        public V2 pos;
        public int color;
    }

    public sealed class World
    {
        public const float Gravity = 18f;
        public const float BallRadius = 0.22f;
        public const float BombRadius = 0.42f;
        const float BombImpact = 9f;
        const float MaxSpeed = 20f;

        public readonly List<Box> walls = new List<Box>();
        public readonly List<Box> lava = new List<Box>();
        public readonly List<Pin> pins = new List<Pin>();
        public readonly List<Body> bodies = new List<Body>();
        public readonly List<Hazard> hazards = new List<Hazard>();
        public readonly List<Peg> pegs = new List<Peg>();
        public readonly List<SimEvent> events = new List<SimEvent>();
        public Jar jar;

        public GameState state = GameState.Playing;
        public LoseReason reason = LoseReason.None;
        public float time;
        public int totalBalls, inJarCount;

        bool settling;
        float allInTimer, calmTimer, noPinTimer;

        // ---------------- xây dựng ----------------

        public void AddWall(float cx, float cy, float w, float h, float deg = 0f) => walls.Add(new Box(cx, cy, w, h, deg));
        public void AddLava(float cx, float cy, float w, float h, float deg = 0f) => lava.Add(new Box(cx, cy, w, h, deg));
        public void AddPin(float tipX, float tipY, float deg, float len) => pins.Add(new Pin(tipX, tipY, deg, len));

        public void AddBalls(float cx, float cy, int cols, int rows, int color)
        {
            float d = BallRadius * 2f + 0.02f;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    float x = cx + (i - (cols - 1) * 0.5f) * d + ((j & 1) == 1 ? d * 0.25f : -d * 0.25f);
                    float y = cy + j * d * 0.9f;
                    bodies.Add(new Body { p = new V2(x, y), r = BallRadius, invM = 1f, color = color, kind = BodyKind.Ball });
                }
        }

        public void AddSpikes(float cx, float cy, float w, float h, float deg = 0f)
            => hazards.Add(new Hazard { kind = HazardKind.Spikes, box = new Box(cx, cy, w, h, deg) });

        public void AddSaw(float x, float y, float r, float ax = 0f, float ay = 0f, float speed = 0f, float phase = 0f)
        {
            var h = new Hazard { kind = HazardKind.Saw, c = new V2(x, y), r = r, speed = speed, phase = phase };
            h.a = new V2(x, y);
            h.b = new V2(x + ax, y + ay);
            if (h.Patrols) { h.a = new V2(x - ax * 0.5f, y - ay * 0.5f); h.b = new V2(x + ax * 0.5f, y + ay * 0.5f); }
            hazards.Add(h);
        }

        public void AddZap(float cx, float cy, float w, float h, float deg, float period, float on, float offset)
            => hazards.Add(new Hazard { kind = HazardKind.Zap, box = new Box(cx, cy, w, h, deg), period = period, on = on, offset = offset });

        public void AddPeg(float x, float y, float r = 0.17f) => pegs.Add(new Peg { c = new V2(x, y), r = r });

        public void AddSpikeBall(float x, float y)
        {
            bodies.Add(new Body { p = new V2(x, y), r = 0.42f, invM = 0.4f, kind = BodyKind.Spike });
        }

        public void AddBomb(float x, float y)
        {
            bodies.Add(new Body { p = new V2(x, y), r = BombRadius, invM = 0.4f, kind = BodyKind.Bomb });
        }

        public void AddFrame()
        {
            AddWall(-4.75f, 0f, 0.7f, 20f);
            AddWall(4.75f, 0f, 0.7f, 20f);
            AddWall(0f, -8.35f, 12f, 1.1f);
            AddWall(0f, 8.2f, 12f, 1f);
        }

        public void SetJar(float cx, float floorY, float w, float h, int color)
        {
            var j = new Jar { cx = cx, floorY = floorY, w = w, h = h, color = color };
            j.back = new Box(cx, floorY + h * 0.5f, w, h, 0f);
            const float t = 0.3f;
            j.glass.Add(new Box(cx, floorY - t * 0.5f, w + t * 2f, t, 0f));
            j.glass.Add(new Box(cx - w * 0.5f - t * 0.5f, floorY + (h - t) * 0.5f, t, h + t, 0f));
            j.glass.Add(new Box(cx + w * 0.5f + t * 0.5f, floorY + (h - t) * 0.5f, t, h + t, 0f));
            // miệng loe để hứng bóng
            float top = floorY + h, wx = w * 0.5f + t * 0.5f;
            j.glass.Add(new Box(cx - wx - 0.3f, top + 0.3f, 1.0f, 0.2f, 135f));
            j.glass.Add(new Box(cx + wx + 0.3f, top + 0.3f, 1.0f, 0.2f, 45f));
            jar = j;
        }

        /// <summary>Lật đối xứng qua trục dọc (trước Begin).</summary>
        public void MirrorX()
        {
            const float pi = (float)Math.PI;
            foreach (var b in walls) FlipBox(b);
            foreach (var b in lava) FlipBox(b);
            foreach (var p in pins)
            {
                p.tip.x = -p.tip.x;
                p.dir.x = -p.dir.x;
                p.box.SetAngle((float)Math.Atan2(p.dir.y, p.dir.x));
                p.box.c = p.Center;
            }
            foreach (var b in bodies) b.p.x = -b.p.x;
            foreach (var h in hazards)
            {
                if (h.box != null) FlipBox(h.box);
                h.c.x = -h.c.x; h.a.x = -h.a.x; h.b.x = -h.b.x;
            }
            foreach (var pg in pegs) pg.c.x = -pg.c.x;
            if (jar != null)
            {
                jar.cx = -jar.cx;
                FlipBox(jar.back);
                foreach (var g in jar.glass) FlipBox(g);
            }
        }

        static void FlipBox(Box b)
        {
            b.c.x = -b.c.x;
            b.SetAngle(-b.ang);   // phản chiếu qua trục dọc, giữ nguyên chiều trên/dưới của hộp
        }

        public void Begin()
        {
            totalBalls = 0;
            foreach (var b in bodies) if (b.kind == BodyKind.Ball) totalBalls++;
            Settle(1.6f);
            events.Clear();
        }

        void Settle(float seconds)
        {
            settling = true;
            const float dt = 1f / 120f;
            for (float t = 0; t < seconds; t += dt) Step(dt);
            settling = false;
            time = 0f;
            foreach (var b in bodies) { b.v = new V2(0, 0); b.flash = 0; }
        }

        // ---------------- điều khiển ----------------

        public bool Pull(int index)
        {
            if (state != GameState.Playing || index < 0 || index >= pins.Count) return false;
            var p = pins[index];
            if (p.state != PinState.Idle) return false;
            p.Pull();
            Emit(EventKind.PinPulled, p.tip, 0);
            return true;
        }

        /// <summary>Chốt gần điểm chạm nhất (còn chưa kéo), -1 nếu không có.</summary>
        public int PickPin(V2 p, float radius)
        {
            int best = -1;
            float bd = radius;
            for (int i = 0; i < pins.Count; i++)
            {
                if (pins[i].state != PinState.Idle) continue;
                float d = pins[i].DistanceTo(p);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public bool AnyPinIdle()
        {
            foreach (var p in pins) if (p.state == PinState.Idle) return true;
            return false;
        }

        // ---------------- mô phỏng ----------------

        void Emit(EventKind k, V2 pos, int color)
        {
            if (settling) return;
            events.Add(new SimEvent { kind = k, pos = pos, color = color });
        }

        public void Step(float dt)
        {
            time += dt;
            foreach (var p in pins) p.Update(dt);
            foreach (var h in hazards) h.Update(time, dt);

            float damp = 1f - 0.25f * dt;
            foreach (var b in bodies)
            {
                if (!b.alive) continue;
                b.v.y -= Gravity * dt;
                b.v = b.v * damp;
                float sp = b.v.Len;
                if (sp > MaxSpeed) b.v = b.v * (MaxSpeed / sp);
                b.p += b.v * dt;
                b.rot -= b.v.x * dt / b.r;
                if (b.flash > 0f) b.flash = Math.Max(0f, b.flash - dt * 2.5f);
            }

            for (int it = 0; it < 4; it++)
            {
                foreach (var b in bodies)
                {
                    if (!b.alive) continue;
                    foreach (var w in walls) Resolve(b, w, default(V2), 0.2f, 0.35f);
                    foreach (var g in jar != null ? jar.glass : EmptyBoxes) Resolve(b, g, default(V2), 0.1f, 0.2f);
                    foreach (var p in pins)
                        if (p.state != PinState.Gone) Resolve(b, p.box, p.Velocity, 0.2f, 0f);
                    foreach (var pg in pegs) ResolvePeg(b, pg);
                }
                for (int i = 0; i < bodies.Count; i++)
                {
                    if (!bodies[i].alive) continue;
                    for (int j = i + 1; j < bodies.Count; j++)
                        if (bodies[j].alive) Resolve(bodies[i], bodies[j]);
                }
            }

            if (state == GameState.Playing && !settling) Rules(dt);
            else if (state == GameState.Lost && !settling) BurnPass();
        }

        // sau khi thua, bóng rơi tiếp vào dung nham vẫn bị đốt cho đúng cảm giác
        void BurnPass()
        {
            foreach (var b in bodies)
            {
                if (!b.alive || b.kind != BodyKind.Ball) continue;
                foreach (var lv in lava)
                    if (lv.Sd(b.p) < b.r * 0.6f)
                    {
                        b.alive = false;
                        Emit(EventKind.Burn, b.p, b.color);
                        break;
                    }
                if (!b.alive) continue;
                foreach (var h in hazards)
                    if (h.Kills(b.p, b.r, time))
                    {
                        b.alive = false;
                        Emit(EventKind.Hurt, b.p, b.color);
                        break;
                    }
            }
        }

        void ResolvePeg(Body b, Peg pg)
        {
            float rr = b.r + pg.r;
            float dx = b.p.x - pg.c.x, dy = b.p.y - pg.c.y;
            float d2 = dx * dx + dy * dy;
            if (d2 >= rr * rr) return;
            float d = (float)Math.Sqrt(d2);
            V2 n = d < 1e-5f ? new V2(0, 1) : new V2(dx / d, dy / d);
            b.p += n * (rr - d);
            float vn = V2.Dot(b.v, n);
            if (vn < 0f)
            {
                float e = -vn > 1.5f ? 0.45f : 0f;
                b.v -= n * ((1f + e) * vn);
                if (b.kind == BodyKind.Bomb && -vn > BombImpact) Explode(b);
            }
        }

        static readonly List<Box> EmptyBoxes = new List<Box>();

        void Resolve(Body b, Box bx, V2 boxVel, float restitution, float friction)
        {
            V2 l = bx.ToLocal(b.p);
            float dx = Math.Abs(l.x) - bx.h.x, dy = Math.Abs(l.y) - bx.h.y;
            if (dx > b.r || dy > b.r) return;

            V2 nl;
            float dist;
            if (dx > 0f || dy > 0f)
            {
                float ox = Math.Max(dx, 0f), oy = Math.Max(dy, 0f);
                dist = (float)Math.Sqrt(ox * ox + oy * oy);
                if (dist >= b.r) return;
                if (dist < 1e-6f) return;
                nl = new V2((l.x < 0 ? -ox : ox) / dist, (l.y < 0 ? -oy : oy) / dist);
            }
            else if (dx > dy)
            {
                nl = new V2(l.x < 0 ? -1f : 1f, 0f);
                dist = dx;
            }
            else
            {
                nl = new V2(0f, l.y < 0 ? -1f : 1f);
                dist = dy;
            }

            V2 n = bx.DirToWorld(nl);
            b.p += n * (b.r - dist);

            V2 vr = b.v - boxVel;
            float vn = V2.Dot(vr, n);
            if (vn >= 0f) return;

            float impact = -vn;
            float e = impact > 1.5f ? restitution : 0f;
            V2 vt = vr - n * vn;
            b.v = b.v - n * ((1f + e) * vn);
            if (friction > 0f)
            {
                float tl = vt.Len;
                if (tl > 1e-5f)
                {
                    float j = Math.Min(tl, friction * (1f + e) * impact);
                    b.v = b.v - vt * (j / tl);
                }
            }

            if (b.kind == BodyKind.Bomb && impact > BombImpact) Explode(b);
        }

        void Resolve(Body a, Body b)
        {
            float rr = a.r + b.r;
            float dx = b.p.x - a.p.x, dy = b.p.y - a.p.y;
            float d2 = dx * dx + dy * dy;
            if (d2 >= rr * rr) return;

            if (a.kind == BodyKind.Spike != (b.kind == BodyKind.Spike) && !settling)
            {
                Body victim = a.kind == BodyKind.Spike ? b : a;
                if (victim.kind == BodyKind.Ball && victim.alive && state == GameState.Playing)
                {
                    victim.alive = false;
                    Emit(EventKind.Hurt, victim.p, victim.color);
                    Lose(LoseReason.Hazard);
                    return;
                }
            }
            float d = (float)Math.Sqrt(d2);
            V2 n = d < 1e-5f ? new V2(0, 1) : new V2(dx / d, dy / d);
            float pen = rr - d;
            float w = a.invM + b.invM;
            a.p -= n * (pen * a.invM / w);
            b.p += n * (pen * b.invM / w);

            float vn = V2.Dot(b.v - a.v, n);
            if (vn < 0f)
            {
                float impact = -vn;
                float e = impact > 1.5f ? 0.2f : 0f;
                float j = -(1f + e) * vn / w;
                a.v -= n * (j * a.invM);
                b.v += n * (j * b.invM);
                if (a.kind == BodyKind.Bomb && impact > BombImpact) Explode(a);
                else if (b.kind == BodyKind.Bomb && impact > BombImpact) Explode(b);
            }

            // trộn màu: bóng trắng chạm bóng màu sẽ đổi sang màu đó
            if (a.kind == BodyKind.Ball && b.kind == BodyKind.Ball && a.color != b.color)
            {
                if (a.color == 0) Convert(a, b.color);
                else if (b.color == 0) Convert(b, a.color);
            }
        }

        void Convert(Body w, int color)
        {
            w.color = color;
            w.flash = 1f;
            Emit(EventKind.Converted, w.p, color);
        }

        void Explode(Body bomb)
        {
            if (settling || !bomb.alive || state != GameState.Playing) return;
            bomb.alive = false;
            Emit(EventKind.Explode, bomb.p, 0);
            foreach (var o in bodies)
            {
                if (!o.alive) continue;
                V2 d = o.p - bomb.p;
                float len = d.Len;
                if (len < 3.2f && len > 1e-4f) o.v += d * (14f * (1f - len / 3.2f) / len);
            }
            Lose(LoseReason.Bomb);
        }

        void Lose(LoseReason r)
        {
            if (state != GameState.Playing) return;
            state = GameState.Lost;
            reason = r;
            Emit(EventKind.Lost, new V2(0, 0), 0);
        }

        void Rules(float dt)
        {
            // dung nham
            foreach (var b in bodies)
            {
                if (!b.alive) continue;
                foreach (var lv in lava)
                {
                    if (lv.Sd(b.p) < b.r * 0.6f)
                    {
                        if (b.kind == BodyKind.Bomb) { Explode(b); }
                        else
                        {
                            b.alive = false;
                            Emit(EventKind.Burn, b.p, b.color);
                            Lose(LoseReason.Lava);
                        }
                        break;
                    }
                }
                if (state != GameState.Playing) return;
            }

            foreach (var b in bodies)
            {
                if (!b.alive || b.kind == BodyKind.Spike) continue;
                foreach (var h in hazards)
                {
                    if (!h.Kills(b.p, b.r, time)) continue;
                    if (b.kind == BodyKind.Bomb) Explode(b);
                    else
                    {
                        b.alive = false;
                        Emit(EventKind.Hurt, b.p, b.color);
                        Lose(LoseReason.Hazard);
                    }
                    break;
                }
                if (state != GameState.Playing) return;
            }

            // bình mục tiêu: bóng sai màu nằm trong bình quá 0.35s => thua
            int inside = 0;
            bool wrongNow = false;
            foreach (var b in bodies)
            {
                if (!b.alive || b.kind != BodyKind.Ball) continue;
                bool inn = jar != null && jar.Contains(b.p);
                if (inn && !b.inJar) Emit(EventKind.BallInJar, b.p, b.color);
                b.inJar = inn;
                b.jarTime = inn ? b.jarTime + dt : 0f;
                if (!inn) continue;
                if (jar.color != 0 && b.color != jar.color)
                {
                    if (b.jarTime > 0.35f) { Lose(LoseReason.WrongColor); return; }
                    wrongNow = true;
                }
                inside++;
            }
            inJarCount = inside;

            if (totalBalls > 0 && inside == totalBalls && !wrongNow)
            {
                allInTimer += dt;
                if (allInTimer > 0.4f)
                {
                    state = GameState.Won;
                    Emit(EventKind.Won, new V2(0, 0), 0);
                    return;
                }
            }
            else allInTimer = 0f;

            // kẹt: hết chốt để kéo mà bóng đã đứng yên
            if (!AnyPinIdle())
            {
                noPinTimer += dt;
                float maxSp = 0f;
                foreach (var b in bodies)
                    if (b.alive) maxSp = Math.Max(maxSp, b.v.Len);
                bool pinsBusy = false;
                foreach (var p in pins) if (p.state == PinState.Pulling) pinsBusy = true;
                if (!pinsBusy && maxSp < 0.45f) calmTimer += dt; else calmTimer = 0f;
                if (calmTimer > 1.3f || noPinTimer > 30f) Lose(LoseReason.Stuck);
            }
        }
    }
}
