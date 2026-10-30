using System;
using UnityEngine;

/// <summary>
/// Âm thanh tổng hợp bằng code (không cần file) + nhạc nền. Mỗi hiệu ứng dùng AudioSource riêng
/// trong một pool nên đổi pitch/độ trễ không ảnh hưởng tiếng khác.
/// </summary>
public sealed class GameAudio
{
    public enum Sfx { Click, Pull, Pop, Convert, Burn, Explode, Win, Lose, Star, Hurt }

    const int Rate = 44100;
    readonly AudioClip[] clips = new AudioClip[10];
    readonly AudioSource[] pool = new AudioSource[10];
    readonly AudioSource bgm;
    int next;
    float bgmTarget = 0.35f;

    public GameAudio(GameObject host, AudioClip bgmClip)
    {
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = host.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
        }
        bgm = host.AddComponent<AudioSource>();
        bgm.playOnAwake = false;
        bgm.loop = true;
        bgm.clip = bgmClip;
        bgm.volume = 0f;
        if (bgmClip != null) bgm.Play();

        var rnd = new System.Random(7);
        float N() => (float)(rnd.NextDouble() * 2.0 - 1.0);

        clips[(int)Sfx.Click] = Make("click", 0.07f, t => Sin(1300f, t) * Mathf.Exp(-t * 55f) * 0.8f);

        clips[(int)Sfx.Pull] = Make("pull", 0.35f, t =>
        {
            float ph = 2f * Mathf.PI * (240f * t + 900f * (1f - Mathf.Exp(-6f * t)) / 6f);
            return (Mathf.Sin(ph) * 0.5f + N() * 0.25f * Mathf.Exp(-t * 12f)) * Mathf.Exp(-t * 7f) * 1.2f;
        });

        clips[(int)Sfx.Pop] = Make("pop", 0.16f, t =>
        {
            float ph = 2f * Mathf.PI * (420f * t + 500f * (1f - Mathf.Exp(-35f * t)) / 35f);
            return Mathf.Sin(ph) * Mathf.Exp(-t * 28f);
        });

        clips[(int)Sfx.Convert] = Make("convert", 0.5f, t =>
            (Sin(740f, t) * Mathf.Exp(-t * 9f) + 0.6f * Sin(1110f, t) * Mathf.Exp(-t * 11f) + 0.3f * Sin(1480f, t) * Mathf.Exp(-t * 14f)) * 0.6f);

        {
            float prev = 0f;
            clips[(int)Sfx.Burn] = Make("burn", 0.55f, t =>
            {
                float n = N(); float hp = n - prev; prev = n;
                return hp * 0.45f * Mathf.Exp(-t * 5f) * (0.6f + 0.4f * Mathf.Sin(t * 190f));
            });
        }
        {
            float lp = 0f;
            clips[(int)Sfx.Explode] = Make("explode", 1.2f, t =>
            {
                lp += 0.07f * (N() - lp);
                float boom = Mathf.Sin(2f * Mathf.PI * (45f * t + 90f * (1f - Mathf.Exp(-7f * t)) / 7f));
                return Mathf.Clamp(lp * 5f * Mathf.Exp(-t * 3.5f) + boom * 0.9f * Mathf.Exp(-t * 4.5f), -1f, 1f);
            });
        }

        clips[(int)Sfx.Win] = Make("win", 1.4f, t =>
        {
            float[] f = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float s = 0f;
            for (int i = 0; i < 4; i++)
            {
                float lt = t - i * 0.13f;
                if (lt < 0f) continue;
                s += (Sin(f[i], lt) + 0.35f * Sin(f[i] * 2f, lt)) * Mathf.Exp(-lt * (i == 3 ? 2.8f : 7f));
            }
            return s * 0.4f;
        });

        clips[(int)Sfx.Lose] = Make("lose", 1.6f, t =>
        {
            float[] f = { 392f, 349.2f, 311.1f, 261.6f };
            float s = 0f;
            for (int i = 0; i < 4; i++)
            {
                float lt = t - i * 0.24f;
                if (lt < 0f) continue;
                float w = Sin(f[i], lt) + 0.5f * Sin(f[i] * 2.01f, lt) + 0.25f * Sin(f[i] * 3f, lt);
                s += w * Mathf.Exp(-lt * (i == 3 ? 2.2f : 5.5f));
            }
            return s * 0.35f;
        });

        {
            float lp2 = 0f;
            clips[(int)Sfx.Hurt] = Make("hurt", 0.45f, t =>
            {
                float f = 1800f * Mathf.Exp(-t * 7f) + 260f;
                float saw = ((f * t) % 1f) * 2f - 1f;
                lp2 += 0.35f * (N() - lp2);
                float zap = Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Sin(2f * Mathf.PI * 1300f * t);
                return (saw * 0.45f + lp2 * 0.5f + zap * 0.35f) * Mathf.Exp(-t * 7f);
            });
        }

        clips[(int)Sfx.Star] = Make("star", 0.4f, t =>
            (Sin(1046f, t) + 0.5f * Sin(1568f, t)) * Mathf.Exp(-t * 8f) * 0.55f);
    }

    static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);

    static AudioClip Make(string name, float seconds, Func<float, float> fn)
    {
        int n = (int)(seconds * Rate);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float fade = Mathf.Clamp01((n - i) / (0.02f * Rate));   // tránh tiếng "tách" cuối clip
            float atk = Mathf.Clamp01(i / (0.002f * Rate));
            data[i] = Mathf.Clamp(fn(t), -1f, 1f) * fade * atk;
        }
        var c = AudioClip.Create(name, n, 1, Rate, false);
        c.SetData(data, 0);
        return c;
    }

    public void Play(Sfx s, float volume = 1f, float pitch = 1f, float delay = 0f)
    {
        if (!Application.isPlaying) return;
        var src = pool[next];
        next = (next + 1) % pool.Length;
        src.Stop();
        src.clip = clips[(int)s];
        src.volume = volume;
        src.pitch = pitch;
        if (delay > 0f) src.PlayDelayed(delay); else src.Play();
    }

    /// <summary>Âm lượng nhạc nền mong muốn (được làm mượt trong Update).</summary>
    public void SetBgm(float target) { bgmTarget = target; }

    public void Update(float dt)
    {
        bgm.volume = Mathf.MoveTowards(bgm.volume, bgmTarget, dt * 0.5f);
    }
}
