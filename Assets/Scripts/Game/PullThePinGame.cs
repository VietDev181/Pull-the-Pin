using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using PullThePin.Sim;

/// <summary>
/// Điều phối game: chạy mô phỏng (PullThePin.Sim), nhận thao tác chạm, rồi đẩy toàn bộ hình khối
/// sang shader "PullThePin/Scene". Không có sprite, UI hay material nào khác — mọi chi tiết do shader vẽ.
/// </summary>
public sealed class PullThePinGame : MonoBehaviour
{
    const int MaxShapes = 192;
    const float Step = 1f / 120f;
    const string LevelKey = "pp_level";
    const string UnlockKey = "pp_unlocked";

    // kind khớp với shader
    const float KWall = 0, KPinShaft = 1, KPinRing = 2, KLava = 3, KBomb = 4, KBall = 5,
                KGlass = 6, KJarBack = 7, KSpark = 8, KShock = 9,
                KSpikes = 10, KSaw = 11, KZap = 12, KSpikeBall = 13, KPeg = 14;

    static readonly Color[] BallColors =
    {
        new Color(0.97f, 0.98f, 1.00f),   // 0 trắng
        new Color(0.96f, 0.22f, 0.28f),   // 1 đỏ
        new Color(0.25f, 0.52f, 1.00f),   // 2 xanh dương
        new Color(0.30f, 0.86f, 0.45f),   // 3 xanh lá
        new Color(1.00f, 0.80f, 0.20f),   // 4 vàng
    };

    struct Particle { public Vector2 p, v; public float life, max, size, gravity; public Color c; }
    struct Shock { public Vector2 p; public float t, dur, maxR; public Color c; }

    Camera cam;
    Material mat;
    Transform quad;
    World world;
    int levelIndex;
    float acc, shake, resultTimer, fade, clock;
    bool pointerHeldOnButton;
    float vx, vy;

    readonly Vector4[] A = new Vector4[MaxShapes];
    readonly Vector4[] B = new Vector4[MaxShapes];
    readonly Vector4[] C = new Vector4[MaxShapes];
    int count;

    readonly List<Particle> particles = new List<Particle>();
    readonly List<Shock> shocks = new List<Shock>();

    GameAudio audio;
    float lastPop, lastBurn;
    int starsPlayed;

    bool ready;
    enum Mode { Title, Select, Play }
    Mode mode = Mode.Title;
    int selPage, unlocked;
    float modeT;
    bool prevHeld, ignoreRelease;
    Vector2 pressStart, lastPos;
    const int TilesPerPage = 15;

    static readonly int idA = Shader.PropertyToID("_PP_A");
    static readonly int idB = Shader.PropertyToID("_PP_B");
    static readonly int idC = Shader.PropertyToID("_PP_C");
    static readonly int idCount = Shader.PropertyToID("_PP_Count");
    static readonly int idView = Shader.PropertyToID("_PP_View");
    static readonly int idHud = Shader.PropertyToID("_PP_Hud");
    static readonly int idHud2 = Shader.PropertyToID("_PP_Hud2");
    static readonly int idHud3 = Shader.PropertyToID("_PP_Hud3");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<PullThePinGame>() != null) return;
        new GameObject("PullThePin").AddComponent<PullThePinGame>();
    }

    void Awake() { Init(); }

    public World World => world;
    public int LevelIndex => levelIndex;

    public void Init()
    {
        if (ready) return;
        ready = true;

        cam = Camera.main;
        if (cam == null)
        {
            var cg = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = cg.AddComponent<Camera>();
            cg.AddComponent<AudioListener>();
        }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;

        var shader = Resources.Load<Shader>("PullThePin");
        if (shader == null) shader = Shader.Find("PullThePin/Scene");
        mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        var go = new GameObject("PP_Screen");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = BuildQuad();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        quad = go.transform;

        SetupAudio();
        unlocked = Mathf.Clamp(Mathf.Max(PlayerPrefs.GetInt(UnlockKey, 1), PlayerPrefs.GetInt(LevelKey, 0) + 1), 1, Levels.Total);
        LoadLevel(Mathf.Min(PlayerPrefs.GetInt(LevelKey, 0), unlocked - 1));
        fade = 0f;
    }

    static Mesh BuildQuad()
    {
        var m = new Mesh { name = "PP_Quad" };
        m.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 1f));
        return m;
    }

    void SetupAudio()
    {
        audio = new GameAudio(gameObject, Resources.Load<AudioClip>("Audio/spring-day-318971"));
    }

    // ------------------------------------------------------------------ màn chơi

    public void LoadLevel(int index)
    {
        levelIndex = Mathf.Max(0, index) % Levels.Total;
        PlayerPrefs.SetInt(LevelKey, levelIndex);
        world = Levels.Build(levelIndex);
        particles.Clear();
        shocks.Clear();
        acc = 0f;
        shake = 0f;
        resultTimer = 0f;
        starsPlayed = 0;
        fade = 1f;
        if (audio != null) audio.SetBgm(0.35f);
    }

    // ------------------------------------------------------------------ vòng lặp

    void Update()
    {
        Tick(Time.deltaTime);
    }

    public void Tick(float dt)
    {
        dt = Mathf.Min(dt, 0.05f);
        clock += dt;
        modeT += dt;
        FitView();

        acc += dt;
        while (acc >= Step)
        {
            world.Step(Step);
            acc -= Step;
        }
        HandleEvents();
        HandleInput();
        UpdateFx(dt);

        if (world.state != GameState.Playing) resultTimer += dt;
        if (world.state == GameState.Won && ResultVisible)
        {
            float rt = resultTimer - ResultDelay;
            while (starsPlayed < 3 && rt > 0.35f + 0.22f * starsPlayed)
            {
                audio.Play(GameAudio.Sfx.Star, 0.7f, 1f + 0.14f * starsPlayed);
                starsPlayed++;
            }
        }
        audio.Update(dt);
        fade = Mathf.Max(0f, fade - dt * 2.2f);

        Upload();
    }

    void FitView()
    {
        float a = Mathf.Max(0.1f, (float)Screen.width / Mathf.Max(1, Screen.height));
        vy = Mathf.Max(9.3f, 5.8f / a);
        vx = vy * a;
        cam.orthographicSize = vy;
        quad.localScale = new Vector3(vx * 2.2f, vy * 2.2f, 1f);
        quad.position = Vector3.zero;
    }

    // ------------------------------------------------------------------ thao tác

    float ResultDelay => world.state == GameState.Won ? 0.8f : 1.3f;
    bool ResultVisible => world.state != GameState.Playing && resultTimer > ResultDelay;
    float HudX => Mathf.Min(vx, 5.5f);

    bool OverButton(Vector2 w)
    {
        if (ResultVisible)
            return Mathf.Abs(w.x) < 1.8f && Mathf.Abs(w.y - -0.7f) < 0.95f;
        return (w - new Vector2(HudX - 1f, vy - 1f)).magnitude < 0.75f;
    }

    Vector2 ToWorld(Vector2 sp)
    {
        return new Vector2((sp.x / Screen.width - 0.5f) * 2f * vx, (sp.y / Screen.height - 0.5f) * 2f * vy);
    }

    bool ReadPointer(out Vector2 pos, out bool pressed, out bool held)
    {
        pos = default; pressed = false; held = false;
        var ts = Touchscreen.current;
        if (ts != null && ts.primaryTouch.press.isPressed)
        {
            pos = ts.primaryTouch.position.ReadValue();
            held = true;
            pressed = ts.primaryTouch.press.wasPressedThisFrame;
            return true;
        }
        var m = Mouse.current;
        if (m != null)
        {
            pos = m.position.ReadValue();
            held = m.leftButton.isPressed;
            pressed = m.leftButton.wasPressedThisFrame;
            return pressed || held;
        }
        return false;
    }

    void SetMode(Mode m)
    {
        mode = m;
        modeT = 0f;
    }

    void OpenSelect()
    {
        int pages = Mathf.CeilToInt(Levels.Total / (float)TilesPerPage);
        selPage = Mathf.Clamp(levelIndex / TilesPerPage, 0, pages - 1);
        SetMode(Mode.Select);
        ignoreRelease = true;      // lần nhả chuột của chính cú chạm mở màn chọn không được tính
        audio.Play(GameAudio.Sfx.Click);
    }

    void HandleSelectTap(Vector2 start, Vector2 end)
    {
        int pages = Mathf.CeilToInt(Levels.Total / (float)TilesPerPage);
        float dx = end.x - start.x;
        if (Mathf.Abs(dx) > 1.5f)
        {
            selPage = Mathf.Clamp(selPage + (dx < 0f ? 1 : -1), 0, pages - 1);
            modeT = 0.1f;
            audio.Play(GameAudio.Sfx.Click, 0.5f);
            return;
        }
        if ((end - new Vector2(HudX - 1f, vy - 1f)).magnitude < 0.85f)
        {
            SetMode(Mode.Play);
            audio.Play(GameAudio.Sfx.Click);
            return;
        }
        for (int k = 0; k < 2; k++)
        {
            float dir = k == 0 ? -1f : 1f;
            if ((end - new Vector2(dir * 3.2f, -8.5f)).magnitude < 1.0f)
            {
                selPage = Mathf.Clamp(selPage + (int)dir, 0, pages - 1);
                modeT = 0.1f;
                audio.Play(GameAudio.Sfx.Click, 0.5f);
                return;
            }
        }
        for (int i = 0; i < TilesPerPage; i++)
        {
            Vector2 c = new Vector2(((i % 3) - 1) * 3f, 5.2f - (i / 3) * 2.9f);
            if (Mathf.Abs(end.x - c.x) > 1.25f || Mathf.Abs(end.y - c.y) > 1.25f) continue;
            int lvl = selPage * TilesPerPage + i;
            if (lvl >= Levels.Total || lvl >= unlocked) return;
            LoadLevel(lvl);
            SetMode(Mode.Play);
            audio.Play(GameAudio.Sfx.Click);
            return;
        }
    }

    void HandleInput()
    {
        var kb = Keyboard.current;
        if (mode == Mode.Play && kb != null && kb.rKey.wasPressedThisFrame) { LoadLevel(levelIndex); return; }

        pointerHeldOnButton = false;
        ReadPointer(out var sp, out bool pressed, out bool held);
        bool released = !held && prevHeld;
        prevHeld = held;
        if (held) lastPos = ToWorld(sp);
        if (pressed) pressStart = ToWorld(sp);

        if (mode == Mode.Title)
        {
            if (pressed) OpenSelect();
            return;
        }
        if (mode == Mode.Select)
        {
            if (released)
            {
                if (ignoreRelease) ignoreRelease = false;
                else HandleSelectTap(pressStart, lastPos);
            }
            return;
        }
        if (!held && !pressed) return;

        Vector2 w = ToWorld(sp);
        if (held && OverButton(w)) pointerHeldOnButton = true;
        if (!pressed) return;

        if (!ResultVisible && (w - new Vector2(-HudX + 1f, vy - 1f)).magnitude < 0.8f)
        {
            OpenSelect();
            return;
        }

        if (OverButton(w))
        {
            audio.Play(GameAudio.Sfx.Click);
            if (ResultVisible)
                LoadLevel(world.state == GameState.Won ? levelIndex + 1 : levelIndex);
            else
                LoadLevel(levelIndex);
            return;
        }

        if (world.state != GameState.Playing) return;
        int pin = world.PickPin(new V2(w.x, w.y), 0.7f);
        if (pin >= 0) world.Pull(pin);
    }

    // ------------------------------------------------------------------ sự kiện, hiệu ứng, âm thanh

    static Color BallColor(int i) => BallColors[Mathf.Clamp(i, 0, BallColors.Length - 1)];

    void HandleEvents()
    {
        var evs = world.events;
        for (int i = 0; i < evs.Count; i++)
        {
            var e = evs[i];
            Vector2 p = new Vector2(e.pos.x, e.pos.y);
            switch (e.kind)
            {
                case EventKind.PinPulled:
                    Burst(p, new Color(1f, 0.85f, 0.35f), 8, 3f, 0.4f);
                    audio.Play(GameAudio.Sfx.Pull, 0.7f, Random.Range(0.95f, 1.1f));
                    break;
                case EventKind.BallInJar:
                    Burst(p, BallColor(e.color), 6, 2.5f, 0.45f);
                    if (clock - lastPop > 0.07f) { lastPop = clock; audio.Play(GameAudio.Sfx.Pop, 0.5f, Random.Range(0.9f, 1.3f)); }
                    break;
                case EventKind.Converted:
                    Burst(p, BallColor(e.color), 3, 2.5f, 0.35f);
                    if (clock - lastPop > 0.08f) { lastPop = clock; audio.Play(GameAudio.Sfx.Convert, 0.45f, Random.Range(0.95f, 1.2f)); }
                    break;
                case EventKind.Burn:
                    Burst(p, new Color(1f, 0.45f, 0.1f), 12, 4.5f, 0.7f);
                    if (clock - lastBurn > 0.2f)
                    {
                        lastBurn = clock;
                        shake = Mathf.Max(shake, 0.12f);
                        audio.Play(GameAudio.Sfx.Burn, 0.6f, Random.Range(0.9f, 1.1f));
                    }
                    break;
                case EventKind.Hurt:
                    Burst(p, new Color(0.8f, 0.9f, 1f), 14, 6f, 0.5f);
                    shake = Mathf.Max(shake, 0.18f);
                    if (clock - lastBurn > 0.2f) { lastBurn = clock; audio.Play(GameAudio.Sfx.Hurt, 0.9f, Random.Range(0.9f, 1.1f)); }
                    break;
                case EventKind.Explode:
                    Burst(p, new Color(1f, 0.65f, 0.2f), 56, 9f, 0.9f);
                    Burst(p, new Color(1f, 0.25f, 0.1f), 24, 5f, 1.1f);
                    shocks.Add(new Shock { p = p, dur = 0.6f, maxR = 3.4f, c = new Color(1f, 0.6f, 0.25f) });
                    shake = Mathf.Max(shake, 0.5f);
                    audio.Play(GameAudio.Sfx.Explode, 1f);
                    break;
                case EventKind.Won:
                    for (int k = 0; k < 5; k++)
                        Burst(new Vector2(Random.Range(-3.5f, 3.5f), Random.Range(1f, 5f)),
                              BallColors[Random.Range(1, BallColors.Length)], 12, 5f, 1.3f);
                    audio.Play(GameAudio.Sfx.Win, 0.9f);
                    audio.SetBgm(0.15f);
                    if (world.state == GameState.Won)
                    {
                        unlocked = Mathf.Min(Mathf.Max(unlocked, levelIndex + 2), Levels.Total);
                        PlayerPrefs.SetInt(UnlockKey, unlocked);
                    }
                    break;
                case EventKind.Lost:
                    audio.Play(GameAudio.Sfx.Lose, 0.8f, 1f, world.reason == LoseReason.Bomb ? 0.7f : 0.4f);
                    audio.SetBgm(0.05f);
                    break;
            }
        }
        evs.Clear();
    }

    void Burst(Vector2 pos, Color c, int n, float speed, float life)
    {
        for (int i = 0; i < n; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float s = speed * Random.Range(0.3f, 1f);
            float l = life * Random.Range(0.5f, 1f);
            particles.Add(new Particle
            {
                p = pos, v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s, life = l, max = l,
                size = Random.Range(0.12f, 0.28f), gravity = 6f,
                c = Color.Lerp(c, Color.white, Random.Range(0f, 0.4f))
            });
        }
        if (particles.Count > 160) particles.RemoveRange(0, particles.Count - 160);
    }

    void UpdateFx(float dt)
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            p.life -= dt;
            if (p.life <= 0f) { particles.RemoveAt(i); continue; }
            p.v.y -= p.gravity * dt;
            p.v *= 1f - 1.5f * dt;
            p.p += p.v * dt;
            particles[i] = p;
        }
        for (int i = shocks.Count - 1; i >= 0; i--)
        {
            var s = shocks[i];
            s.t += dt;
            if (s.t >= s.dur) { shocks.RemoveAt(i); continue; }
            shocks[i] = s;
        }
        shake = Mathf.Max(0f, shake - dt * 1.4f);
    }

    // ------------------------------------------------------------------ đẩy dữ liệu sang shader

    void Add(float kind, float x, float y, float ang, float bx, float by, float bz, float bw, Color c)
    {
        if (count >= MaxShapes) return;
        A[count] = new Vector4(kind, x, y, ang);
        B[count] = new Vector4(bx, by, bz, bw);
        C[count] = new Vector4(c.r, c.g, c.b, c.a);
        count++;
    }

    void AddBox(float kind, Box b, float round, float param, Color c)
        => Add(kind, b.c.x, b.c.y, b.ang, b.h.x, b.h.y, round, param, c);

    void Upload()
    {
        count = 0;
        var w = world;

        // lòng bình
        if (w.jar != null)
        {
            Color jc = w.jar.color > 0 ? BallColor(w.jar.color) : new Color(0.55f, 0.8f, 1f, 0f);
            jc.a = w.jar.color > 0 ? 1f : 0f;
            AddBox(KJarBack, w.jar.back, 0f, 0f, jc);
        }
        // thân chốt (nằm dưới tường để trông như trượt ra từ bên trong)
        foreach (var p in w.pins)
            if (p.state != PinState.Gone)
                AddBox(KPinShaft, p.box, 0f, 0f, Color.white);
        foreach (var b in w.walls)
            AddBox(KWall, b, Mathf.Min(0.08f, Mathf.Min(b.h.x, b.h.y)), 0f, Color.white);
        // vòng kéo nằm trên tường
        foreach (var p in w.pins)
        {
            if (p.state == PinState.Gone) continue;
            V2 rc = p.RingCenter;
            Add(KPinRing, rc.x, rc.y, p.box.ang, Pin.RingRadius, Pin.RingRadius * 0.55f,
                p.state == PinState.Idle ? 1f : 0f, 0f, Color.white);
        }
        foreach (var pg in w.pegs)
            Add(KPeg, pg.c.x, pg.c.y, 0f, pg.r, 0f, 0f, 0f, Color.white);
        foreach (var l in w.lava)
            AddBox(KLava, l, 0f, 0f, Color.white);
        foreach (var h in w.hazards)
        {
            switch (h.kind)
            {
                case HazardKind.Spikes: AddBox(KSpikes, h.box, 0f, 0f, Color.white); break;
                case HazardKind.Saw: Add(KSaw, h.c.x, h.c.y, h.rot, h.r, 0f, 0f, 0f, Color.white); break;
                case HazardKind.Zap: AddBox(KZap, h.box, h.ZapState(w.time), 0f, Color.white); break;
            }
        }

        foreach (var b in w.bodies)
            if (b.alive && b.kind == BodyKind.Bomb)
                Add(KBomb, b.p.x, b.p.y, b.rot, b.r, 0f, 0f, 0f, Color.white);
        foreach (var b in w.bodies)
            if (b.alive && b.kind == BodyKind.Spike)
                Add(KSpikeBall, b.p.x, b.p.y, b.rot, b.r, 0f, 0f, 0f, Color.white);
        foreach (var b in w.bodies)
            if (b.alive && b.kind == BodyKind.Ball)
                Add(KBall, b.p.x, b.p.y, b.rot, b.r, 0f, b.flash, 0f, BallColor(b.color));

        if (w.jar != null)
            foreach (var g in w.jar.glass)
                AddBox(KGlass, g, Mathf.Min(0.07f, Mathf.Min(g.h.x, g.h.y)), 0f, Color.white);

        foreach (var s in shocks)
        {
            float k = s.t / s.dur;
            Color c = s.c; c.a = (1f - k) * 0.9f;
            Add(KShock, s.p.x, s.p.y, 0f, s.maxR * Mathf.Sqrt(k), 0.12f + 0.25f * k, 0f, 0f, c);
        }
        foreach (var p in particles)
        {
            Color c = p.c; c.a = Mathf.Clamp01(p.life / p.max);
            Add(KSpark, p.p.x, p.p.y, 0f, p.size * (0.5f + 0.5f * c.a), 0f, 0f, 0f, c);
        }

        Vector2 off = shake > 0f ? Random.insideUnitCircle * shake : Vector2.zero;
        float stateIdx = mode == Mode.Title ? 3f : mode == Mode.Select ? 4f : world.state == GameState.Playing || !ResultVisible ? 0f : world.state == GameState.Won ? 1f : 2f;

        Shader.SetGlobalVectorArray(idA, A);
        Shader.SetGlobalVectorArray(idB, B);
        Shader.SetGlobalVectorArray(idC, C);
        Shader.SetGlobalVector(idCount, new Vector4(count, 0, 0, 0));
        Shader.SetGlobalVector(idView, new Vector4(vx, vy, off.x, off.y));
        int pagesCount = Mathf.CeilToInt(Levels.Total / (float)TilesPerPage);
        if (mode == Mode.Select) Shader.SetGlobalVector(idHud, new Vector4(selPage, pagesCount, unlocked, stateIdx));
        else Shader.SetGlobalVector(idHud, new Vector4(levelIndex + 1, w.inJarCount, w.totalBalls, stateIdx));
        Shader.SetGlobalVector(idHud3, new Vector4(levelIndex, Levels.Total, 0f, 0f));
        Shader.SetGlobalVector(idHud2, new Vector4(mode != Mode.Play ? modeT : Mathf.Max(0f, resultTimer - ResultDelay), clock, fade, pointerHeldOnButton ? 1f : 0f));
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }
}
