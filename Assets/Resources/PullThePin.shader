// Pull the Pin — toàn bộ hình ảnh của game (nền, tường, chốt, bóng, bom, dung nham, bình, hiệu ứng, HUD)
// được vẽ bằng MỘT shader SDF toàn màn hình. CPU chỉ gửi danh sách hình khối + vài tham số HUD.
//
//  _PP_A[i] = (kind, x, y, angle)
//  _PP_B[i] = (hx, hy, extra, param)
//  _PP_C[i] = color rgba
//
//  kind: 0 tường | 1 thân chốt | 2 vòng chốt | 3 dung nham | 4 bom | 5 bóng
//        6 kính bình | 7 lòng bình | 8 tia lửa | 9 sóng xung kích
//        10 gai | 11 cưa quay | 12 hàng rào điện | 13 bóng gai | 14 cọc đẩy
Shader "PullThePin/Scene"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "PullThePinScene"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAXS 192

            float4 _PP_A[MAXS];
            float4 _PP_B[MAXS];
            float4 _PP_C[MAXS];
            float4 _PP_Count;   // x = số hình
            float4 _PP_View;    // x,y = nửa khung nhìn; zw = rung màn hình
            float4 _PP_Hud;     // x = màn, y = bóng trong bình, z = tổng bóng, w = 0 chơi | 1 thắng | 2 thua
            float4 _PP_Hud3;    // x = màn đang chơi (0-based), y = tổng số màn
            float4 _PP_Hud2;    // x = thời gian bảng kết quả, y = thời gian game, z = mờ đen, w = nhấn nút (0/1)

            static float g_px;

            struct Attr { float4 pos : POSITION; };
            struct Vary { float4 pos : SV_POSITION; float2 w : TEXCOORD0; };

            Vary vert(Attr i)
            {
                Vary o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.w = TransformObjectToWorld(i.pos.xyz).xy;
                return o;
            }

            // ------------------------------------------------------------------ tiện ích
            float cov(float d) { return saturate(0.5 - d / g_px); }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { v += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            float2 rot2(float2 v, float a)
            {
                float c = cos(a), s = sin(a);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            float sdBox(float2 p, float2 h)
            {
                float2 d = abs(p) - h;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            float sdSeg(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            float sdTri(float2 p, float2 p0, float2 p1, float2 p2)
            {
                float2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
                float2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
                float2 pq0 = v0 - e0 * saturate(dot(v0, e0) / dot(e0, e0));
                float2 pq1 = v1 - e1 * saturate(dot(v1, e1) / dot(e1, e1));
                float2 pq2 = v2 - e2 * saturate(dot(v2, e2) / dot(e2, e2));
                float s = e0.x * e2.y - e0.y * e2.x;
                s = s < 0.0 ? -1.0 : 1.0;
                float2 d = min(min(float2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                                   float2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                                   float2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
                return -sqrt(d.x) * (d.y < 0.0 ? -1.0 : 1.0);
            }

            float sdStar5(float2 p, float r, float rf)
            {
                const float2 k1 = float2(0.809016994375, -0.587785252292);
                const float2 k2 = float2(-0.809016994375, -0.587785252292);
                p.x = abs(p.x);
                p -= 2.0 * max(dot(k1, p), 0.0) * k1;
                p -= 2.0 * max(dot(k2, p), 0.0) * k2;
                p.x = abs(p.x);
                p.y -= r;
                float2 ba = rf * float2(-k1.y, k1.x) - float2(0.0, 1.0);
                float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
                float2 q = p - ba * h;
                float s = p.y * ba.x - p.x * ba.y;
                return length(q) * (s < 0.0 ? -1.0 : 1.0);
            }

            // 7 đoạn: bit0 a(trên) 1 b(phải trên) 2 c(phải dưới) 3 d(dưới) 4 e(trái dưới) 5 f(trái trên) 6 g(giữa)
            static const int kMasks[10] = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

            float sdDigit(float2 p, int n)
            {
                int m = kMasks[clamp(n, 0, 9)];
                float d = 1e3;
                if ((m & 1)  != 0) d = min(d, sdSeg(p, float2(-0.2, 0.45),  float2(0.2, 0.45)));
                if ((m & 2)  != 0) d = min(d, sdSeg(p, float2(0.25, 0.40),  float2(0.25, 0.05)));
                if ((m & 4)  != 0) d = min(d, sdSeg(p, float2(0.25, -0.05), float2(0.25, -0.40)));
                if ((m & 8)  != 0) d = min(d, sdSeg(p, float2(-0.2, -0.45), float2(0.2, -0.45)));
                if ((m & 16) != 0) d = min(d, sdSeg(p, float2(-0.25, -0.05), float2(-0.25, -0.40)));
                if ((m & 32) != 0) d = min(d, sdSeg(p, float2(-0.25, 0.40), float2(-0.25, 0.05)));
                if ((m & 64) != 0) d = min(d, sdSeg(p, float2(-0.2, 0.0),   float2(0.2, 0.0)));
                return d - 0.055;
            }

            // khoảng cách tới số n (1–2 chữ số), p theo đơn vị thế giới, h = chiều cao chữ
            float sdNumber(float2 p, int n, float h)
            {
                p /= h;
                if (n >= 100)
                {
                    float d0 = sdDigit(p - float2(-0.68, 0.0), (n / 100) % 10);
                    float d1 = sdDigit(p - float2(0.0, 0.0), (n / 10) % 10);
                    float d2 = sdDigit(p - float2(0.68, 0.0), n % 10);
                    return min(min(d0, d1), d2) * h;
                }
                if (n >= 10)
                {
                    float d0 = sdDigit(p - float2(-0.33, 0.0), (n / 10) % 10);
                    float d1 = sdDigit(p - float2(0.33, 0.0), n % 10);
                    return min(d0, d1) * h;
                }
                return sdDigit(p, n) * h;
            }

            // ------------------------------------------------------------------ nền
            float3 Background(float2 p, float t)
            {
                float vy = _PP_View.y;
                float y = saturate(p.y / (vy * 2.0) + 0.5);
                float3 c = lerp(float3(0.05, 0.20, 0.34), float3(0.24, 0.12, 0.42), pow(y, 0.8));
                c += float3(0.45, 0.25, 0.55) * 0.18 * exp(-length((p - float2(0.0, 3.0)) * float2(0.18, 0.12)));

                // lưới mờ
                float2 gv = abs(frac(p * 0.5) - 0.5);
                float grid = smoothstep(0.47, 0.5, max(gv.x, gv.y));
                c += 0.012 * grid;


                // bánh răng cơ khí phía sau
                for (int gI = 0; gI < 3; gI++)
                {
                    float2 gc = gI == 0 ? float2(-3.3, 5.8) : (gI == 1 ? float2(3.9, -1.0) : float2(-2.4, -6.3));
                    float R = gI == 0 ? 2.5 : (gI == 1 ? 3.2 : 2.0);
                    float N = gI == 0 ? 14.0 : (gI == 1 ? 16.0 : 12.0);
                    float dirG = gI == 1 ? -1.0 : 1.0;
                    float2 gq = p - gc;
                    float ga = atan2(gq.y, gq.x) + dirG * t * 0.10 * (3.0 / R);
                    float gd = length(gq) - R * (1.0 + 0.085 * smoothstep(-0.25, 0.25, sin(ga * N)));
                    float hole = abs(length(gq) - R * 0.6) - 0.06;
                    float spokes = 1e3;
                    float hub = length(gq) - R * 0.16;
                    float sp = abs(sin(ga * 3.0)) ;
                    float spokeD = max(length(gq) - R * 0.6, 0.0) * 0 + (abs(sin(ga * 3.0)) * length(gq) - 0.12);
                    spokeD = max(spokeD, length(gq) - R * 0.6);
                    float body = cov(gd);
                    c = lerp(c, c * 1.28 + float3(0.012, 0.02, 0.05), body * 0.7);
                    c += float3(0.35, 0.5, 1.0) * 0.10 * exp(-abs(gd) * 38.0);
                    c = lerp(c, c * 0.55, cov(min(hole, hub)) * body * 0.6);
                }
                // ống dẫn có ánh sáng chạy
                for (int pI = 0; pI < 2; pI++)
                {
                    float px = pI == 0 ? -4.05 : 4.05;
                    float dpp = abs(p.x - px) - 0.11;
                    float inF = step(abs(p.y + 0.05), 7.6);
                    float3 pc = lerp(float3(0.06, 0.08, 0.17), float3(0.16, 0.2, 0.36), saturate(1.0 - abs(p.x - px) / 0.11));
                    c = lerp(c, pc, cov(dpp) * inF * 0.9);
                    float flow = smoothstep(0.82, 1.0, sin(p.y * 0.9 - t * (pI == 0 ? 1.6 : -1.3) + pI * 2.0));
                    c += float3(0.2, 0.8, 1.0) * flow * cov(dpp + 0.05) * inF * 0.35;
                    float jn = smoothstep(0.03, 0.0, abs(frac(p.y / 2.5 + 0.5) - 0.5) * 2.5 - 0.1);
                    c = lerp(c, float3(0.25, 0.3, 0.5), jn * cov(abs(p.x - px) - 0.16) * inF * 0.6);
                }
                // bokeh trôi chậm
                for (int l = 0; l < 3; l++)
                {
                    float sc = 0.28 + l * 0.17;
                    float2 g = p * sc + float2(l * 17.0, t * (0.03 + 0.02 * l));
                    float2 id = floor(g);
                    float h = hash21(id + l * 7.0);
                    float2 ctr = id + 0.5 + (float2(h, hash21(id + 3.3)) - 0.5) * 0.6;
                    float rad = 0.12 + 0.22 * h;
                    float b = smoothstep(rad, rad * 0.35, length(g - ctr));
                    c += float3(0.55, 0.70, 1.0) * 0.2 * b * step(0.42, h);
                }

                // khu vực chơi sáng hơn
                float fd = sdBox(p - float2(0.0, -0.05), float2(4.4, 7.75));
                c = lerp(c, c * 0.72 + float3(0.02, 0.03, 0.09), saturate(0.5 - fd / g_px));
                c += float3(0.6, 0.7, 1.0) * 0.10 * exp(-abs(fd) * 14.0);
                c *= 1.0 - 0.40 * pow(saturate(length(p / float2(_PP_View.x, vy)) * 0.75), 2.5);
                return c;
            }

            // ------------------------------------------------------------------ HUD
            float3 Lamps(float3 col, float2 p, float t)
            {
                if (abs(p.y + 0.05) > 7.5) return col;
                float sx = p.x < 0.0 ? -1.0 : 1.0;
                float ly = round((p.y + 1.25) / 2.5) * 2.5 - 1.25;
                float2 q = p - float2(sx * 4.75, ly);
                float h = hash21(float2(sx, ly));
                float blink = 0.55 + 0.45 * sin(t * (1.5 + h * 2.0) + h * 40.0);
                float3 lc = h < 0.4 ? float3(0.2, 0.9, 1.0) : (h < 0.75 ? float3(1.0, 0.7, 0.2) : float3(1.0, 0.25, 0.3));
                float dl = length(q);
                col += lc * exp(-dl * 6.0) * 0.4 * blink;
                col = lerp(col, float3(0.05, 0.04, 0.1), cov(dl - 0.14));
                col = lerp(col, lc * (0.6 + 0.6 * blink), cov(dl - 0.095));
                col += float3(1, 1, 1) * cov(length(q - float2(-0.03, 0.03)) - 0.025) * 0.6;
                return col;
            }

            float3 Hud(float3 col, float2 p, float t)
            {
                float hx = min(_PP_View.x, 5.5);
                float topY = _PP_View.y - 1.0;
                float3 gold = float3(1.0, 0.80, 0.30);

                // thanh nền trên cùng che khung tường
                {
                    float dd = (_PP_View.y - 1.95) - p.y;
                    float3 pc = lerp(float3(0.05, 0.06, 0.16), float3(0.09, 0.09, 0.24), saturate(-dd / 1.9));
                    col = lerp(col, pc, cov(dd));
                    col = lerp(col, gold, cov(abs(dd) - 0.03) * 0.9);
                    col *= 1.0 - 0.35 * exp(-max(dd, 0.0) * 5.0) * (1.0 - cov(dd));
                }

                // huy hiệu màn chơi
                {
                    float2 q = p - float2(-hx + 1.0, topY);
                    float d = length(q) - 0.58;
                    col = lerp(col, float3(0.06, 0.08, 0.17), cov(d) * 0.92);
                    col = lerp(col, gold, cov(abs(d + 0.04) - 0.045));
                    float dn = sdNumber(q, (int)(_PP_Hud.x + 0.5), 0.46);
                    col = lerp(col, float3(1, 1, 1), cov(dn));
                }

                // thanh tiến độ
                {
                    float2 q = p - float2(0.0, topY);
                    float2 hb = float2(2.0, 0.24);
                    float d = sdBox(q, hb - 0.24) - 0.24;
                    float prog = _PP_Hud.z > 0.5 ? saturate(_PP_Hud.y / _PP_Hud.z) : 0.0;
                    col = lerp(col, float3(0.05, 0.07, 0.16), cov(d) * 0.92);
                    float fx = -hb.x + 0.06 + prog * (hb.x * 2.0 - 0.12);
                    float df = max(sdBox(q, hb - 0.24 - 0.06) - 0.18, q.x - fx);
                    float3 fc = lerp(float3(0.25, 0.85, 0.45), float3(1.0, 0.9, 0.35), prog);
                    fc += 0.25 * smoothstep(0.0, 0.2, q.y) * smoothstep(0.24, 0.1, q.y);
                    col = lerp(col, fc, cov(df) * step(0.001, prog));
                    col = lerp(col, gold * 0.9, cov(abs(d + 0.025) - 0.025));
                    // biểu tượng bóng ở đầu thanh
                    float2 bq = q - float2(-hb.x - 0.42, 0.0);
                    float db = length(bq) - 0.24;
                    float3 bc = lerp(float3(0.75, 0.85, 1.0), float3(1, 1, 1), smoothstep(0.1, -0.2, bq.y - bq.x * 0.5));
                    col = lerp(col, bc, cov(db));
                }

                // nút chơi lại
                {
                    float press = _PP_Hud2.w > 0.5 ? 0.92 : 1.0;
                    float2 q = (p - float2(hx - 1.0, topY)) / press;
                    float d = length(q) - 0.58;
                    col = lerp(col, float3(0.06, 0.08, 0.17), cov(d) * 0.92);
                    col = lerp(col, gold, cov(abs(d + 0.04) - 0.045));
                    float R = 0.26;
                    float a = atan2(q.y, q.x);
                    float ring = abs(length(q) - R) - 0.05;
                    float gapA = abs(a - radians(48.0));
                    ring = max(ring, 0.1 - gapA * R * 1.6);
                    float2 P = R * float2(cos(radians(70.0)), sin(radians(70.0)));
                    float2 tg = float2(sin(radians(70.0)), -cos(radians(70.0)));
                    float2 nr = float2(cos(radians(70.0)), sin(radians(70.0)));
                    float dt2 = sdTri(q, P + tg * 0.20, P + nr * 0.13, P - nr * 0.13);
                    col = lerp(col, float3(1, 1, 1), cov(min(ring, dt2)));
                }
                return col;
            }

            float sdLetter(float2 p, int id)
            {
                float d = 1e3;
                if (id == 0) { // P
                    d = min(sdSeg(p, float2(-.3,-.5), float2(-.3,.5)), sdSeg(p, float2(-.3,.5), float2(.12,.5)));
                    d = min(d, sdSeg(p, float2(.12,.5), float2(.3,.33)));
                    d = min(d, sdSeg(p, float2(.3,.33), float2(.3,.17)));
                    d = min(d, sdSeg(p, float2(.3,.17), float2(.12,0)));
                    d = min(d, sdSeg(p, float2(.12,0), float2(-.3,0)));
                } else if (id == 1) { // U
                    d = min(sdSeg(p, float2(-.3,.5), float2(-.3,-.32)), sdSeg(p, float2(-.3,-.32), float2(-.14,-.5)));
                    d = min(d, sdSeg(p, float2(-.14,-.5), float2(.14,-.5)));
                    d = min(d, sdSeg(p, float2(.14,-.5), float2(.3,-.32)));
                    d = min(d, sdSeg(p, float2(.3,-.32), float2(.3,.5)));
                } else if (id == 2) { // L
                    d = min(sdSeg(p, float2(-.3,.5), float2(-.3,-.5)), sdSeg(p, float2(-.3,-.5), float2(.3,-.5)));
                } else if (id == 3) { // T
                    d = min(sdSeg(p, float2(-.32,.5), float2(.32,.5)), sdSeg(p, float2(0,.5), float2(0,-.5)));
                } else if (id == 4) { // H
                    d = min(sdSeg(p, float2(-.3,.5), float2(-.3,-.5)), sdSeg(p, float2(.3,.5), float2(.3,-.5)));
                    d = min(d, sdSeg(p, float2(-.3,0), float2(.3,0)));
                } else if (id == 5) { // E
                    d = min(sdSeg(p, float2(-.3,.5), float2(-.3,-.5)), sdSeg(p, float2(-.3,.5), float2(.3,.5)));
                    d = min(d, sdSeg(p, float2(-.3,0), float2(.2,0)));
                    d = min(d, sdSeg(p, float2(-.3,-.5), float2(.3,-.5)));
                } else if (id == 6) { // I
                    d = min(sdSeg(p, float2(0,.5), float2(0,-.5)), sdSeg(p, float2(-.17,.5), float2(.17,.5)));
                    d = min(d, sdSeg(p, float2(-.17,-.5), float2(.17,-.5)));
                } else if (id == 8) { // V
                    d = min(sdSeg(p, float2(-.3,.5), float2(0,-.5)), sdSeg(p, float2(0,-.5), float2(.3,.5)));
                } else if (id == 9) { // S
                    d = min(sdSeg(p, float2(.3,.5), float2(-.3,.5)), sdSeg(p, float2(-.3,.5), float2(-.3,0)));
                    d = min(d, sdSeg(p, float2(-.3,0), float2(.3,0)));
                    d = min(d, sdSeg(p, float2(.3,0), float2(.3,-.5)));
                    d = min(d, sdSeg(p, float2(.3,-.5), float2(-.3,-.5)));
                } else if (id == 7) { // N
                    d = min(sdSeg(p, float2(-.3,-.5), float2(-.3,.5)), sdSeg(p, float2(-.3,.5), float2(.3,-.5)));
                    d = min(d, sdSeg(p, float2(.3,-.5), float2(.3,.5)));
                }
                return d;
            }

            // n chữ (3 hoặc 4) căn giữa, mỗi chữ rung nhẹ theo thời gian
            float sdWord(float2 p, int n, int a, int b, int c, int e, float adv, float t)
            {
                float d = 1e3;
                int ids[4] = { a, b, c, e };
                for (int i = 0; i < 4; i++)
                {
                    if (i >= n) break;
                    float x = (i - (n - 1) * 0.5) * adv;
                    float2 q = p - float2(x, 0.07 * sin(t * 3.0 + i * 1.3));
                    q = rot2(q, 0.06 * sin(t * 2.0 + i * 2.1));
                    d = min(d, sdLetter(q, ids[i]));
                }
                return d - 0.11;
            }

            float3 Ink(float3 col, float d, float sc, float yy)
            {
                float dd = d * sc;
                col = lerp(col, float3(0.05, 0.02, 0.12), cov(dd - 0.07 * sc * 0.6 - 0.0) * 0.9);
                float3 fill = lerp(float3(1.0, 0.45, 0.12), float3(1.0, 0.92, 0.35), saturate(yy * 0.5 + 0.55));
                col = lerp(col, fill, cov(dd));
                col += float3(1, 1, 0.8) * 0.35 * cov(dd) * smoothstep(0.1, 0.5, yy);
                return col;
            }

            float3 Menu(float3 col, float2 p)
            {
                float t = _PP_Hud2.x;
                col *= 0.28;
                float s = saturate(t / 0.6);
                float pop = 1.0 + 1.2 * pow(s - 1.0, 3.0) + 0.5 * pow(s - 1.0, 2.0);

                // tiêu đề
                float sc1 = 1.55 * pop;
                float2 q1 = (p - float2(0.0, 4.7)) / sc1;
                col = Ink(col, sdWord(q1, 4, 0, 1, 2, 2, 0.88, t), sc1, q1.y);
                float sc2 = 1.2 * pop;
                float2 q2a = (p - float2(-2.15, 2.75)) / sc2;
                float2 q2b = (p - float2(2.1, 2.75)) / sc2;
                float d2 = min(sdWord(q2a, 3, 3, 4, 5, 0, 0.86, t + 1.0), sdWord(q2b, 3, 0, 6, 7, 0, 0.86, t + 2.0));
                col = Ink(col, d2, sc2, q2a.y);

                // chốt vàng trang trí
                {
                    float2 q = p - float2(0.0, 0.7);
                    float dsh = sdBox(q - float2(0.35, 0.0), float2(2.1, 0.12) - 0.12) - 0.12;
                    float u = clamp(q.y / 0.12, -1.0, 1.0);
                    float3 metal = float3(0.93, 0.74, 0.26) * (0.5 + 0.6 * sqrt(max(1.0 - u * u, 0.0))) + pow(saturate(1.0 - abs(u - 0.4) * 3.0), 3.0) * 0.5;
                    metal = lerp(metal, float3(0.3, 0.17, 0.02), smoothstep(-0.035, -0.005, dsh));
                    col = lerp(col, metal, cov(dsh) * s);
                    float dr = max(length(q - float2(-2.0, 0.0)) - 0.42, 0.22 - length(q - float2(-2.0, 0.0)));
                    col = lerp(col, float3(1.0, 0.82, 0.3), cov(dr) * s);
                    col = lerp(col, float3(0.3, 0.17, 0.02), cov(abs(dr + 0.0) - 0.02) * 0.6 * s);
                }

                // nút chơi
                float pul = 1.0 + 0.045 * sin(t * 4.0);
                float2 bq = (p - float2(0.0, -2.9)) / (pul * pop);
                float bd = sdBox(bq, float2(2.2, 0.95) - 0.95) - 0.95;
                col += float3(0.3, 1.0, 0.5) * exp(-max(bd, 0.0) * 3.0) * 0.35;
                col = lerp(col, float3(0.08, 0.35, 0.18), cov(bd));
                float bd2 = sdBox(bq - float2(0.0, 0.09), float2(2.2, 0.95) - 0.95) - 0.95;
                float3 bc = lerp(float3(0.20, 0.72, 0.36), float3(0.45, 0.95, 0.50), saturate(bq.y * 0.5 + 0.5));
                col = lerp(col, bc, cov(bd2 + 0.05));
                col += 0.2 * cov(bd2 + 0.05) * smoothstep(0.2, 0.8, bq.y);
                float dt2 = sdTri(bq, float2(-0.3, -0.5), float2(-0.3, 0.6), float2(0.62, 0.05)) - 0.08;
                col = lerp(col, float3(1, 1, 1), cov(dt2 - 0.0));
                return col;
            }

            float3 SelectScreen(float3 col, float2 p)
            {
                float t = _PP_Hud2.x;
                float vy = _PP_View.y;
                float hx = min(_PP_View.x, 5.5);
                col *= 0.30;
                float page = _PP_Hud.x, pages = _PP_Hud.y, unlocked = _PP_Hud.z;
                float cur = _PP_Hud3.x, total = _PP_Hud3.y;
                float3 gold = float3(1.0, 0.80, 0.30);

                // tiêu đề LEVELS
                {
                    float sc = 0.85;
                    float2 q = (p - float2(0.0, vy - 1.25)) / sc;
                    float d = min(sdWord(q - float2(-1.3, 0.0), 3, 2, 5, 8, 0, 0.86, t),
                                  sdWord(q - float2(1.3, 0.0), 3, 5, 2, 9, 0, 0.86, t + 1.0));
                    col = Ink(col, d, sc, q.y);
                }

                // lưới ô màn chơi 3 x 5
                for (int i = 0; i < 15; i++)
                {
                    float2 c = float2(((i % 3) - 1) * 3.0, 5.2 - (i / 3) * 2.9);
                    float lvl = page * 15.0 + i;
                    if (lvl >= total) continue;
                    float ap = saturate((t - 0.035 * i) / 0.3);
                    float sc = 1.0 + 1.6 * pow(ap - 1.0, 3.0) + 0.6 * pow(ap - 1.0, 2.0);
                    if (ap < 0.001) continue;
                    float2 q = (p - c) / sc;
                    float d = sdBox(q, float2(1.2, 1.2) - 0.35) - 0.35;
                    bool locked = lvl >= unlocked;
                    bool isCur = abs(lvl - cur) < 0.5;
                    bool done = lvl < unlocked - 1.0;
                    float3 base = locked ? float3(0.16, 0.17, 0.30) : (isCur ? float3(0.22, 0.74, 0.40) : float3(0.30, 0.40, 0.86));
                    col *= 1.0 - 0.45 * cov((sdBox(q - float2(0.07, -0.12), float2(1.2, 1.2) - 0.35) - 0.35) * sc);
                    float3 f = lerp(base * 0.55, base, saturate(q.y * 0.45 + 0.55));
                    f += 0.22 * smoothstep(0.35, 1.0, q.y) * (locked ? 0.3 : 1.0);
                    float rim = smoothstep(-0.12, -0.02, d);
                    f = lerp(f, locked ? float3(0.3, 0.32, 0.5) : (isCur ? float3(0.7, 1.0, 0.75) : gold), rim * 0.9);
                    f = lerp(f, float3(0.05, 0.05, 0.14), smoothstep(-0.05, -0.008, d));
                    col = lerp(col, f, cov(d * sc));

                    if (!locked)
                    {
                        float dn = sdNumber(q - float2(0.0, -0.05), (int)(lvl + 1.5), 0.78);
                        col = lerp(col, float3(0.05, 0.05, 0.14), cov((dn - 0.07) * sc) * 0.8);
                        col = lerp(col, float3(1, 1, 1), cov(dn * sc));
                        if (done)
                        {
                            float ds = sdStar5(rot2(q - float2(0.78, 0.8), 0.0) / 0.28, 1.0, 0.45) * 0.28;
                            col = lerp(col, float3(0.05, 0.04, 0.1), cov((ds - 0.05) * sc));
                            col = lerp(col, float3(1.0, 0.82, 0.25), cov(ds * sc));
                        }
                    }
                    else
                    {
                        float2 lq = q - float2(0.0, -0.1);
                        float body = sdBox(lq, float2(0.42, 0.34) - 0.08) - 0.08;
                        float2 sq = lq - float2(0.0, 0.3);
                        float shackle = max(abs(length(sq) - 0.27) - 0.075, -sq.y - 0.02);
                        float3 lc = float3(0.62, 0.65, 0.82);
                        col = lerp(col, lc, cov(min(body, shackle) * sc));
                        col = lerp(col, float3(0.15, 0.16, 0.3), cov((length(lq - float2(0.0, -0.02)) - 0.07) * sc));
                    }
                }

                // mũi tên chuyển trang
                for (int k = 0; k < 2; k++)
                {
                    float dir = k == 0 ? -1.0 : 1.0;
                    bool ok = k == 0 ? page > 0.5 : page < pages - 1.5;
                    float2 q = p - float2(dir * 3.2, -8.5);
                    float d = length(q) - 0.78;
                    float3 bc = ok ? float3(0.30, 0.40, 0.86) : float3(0.16, 0.17, 0.30);
                    col = lerp(col, bc, cov(d));
                    col = lerp(col, ok ? gold : float3(0.3, 0.32, 0.5), cov(abs(d + 0.05) - 0.04));
                    float dt2 = sdTri(q, float2(dir * 0.28, 0.0), float2(-dir * 0.22, 0.32), float2(-dir * 0.22, -0.32)) - 0.05;
                    col = lerp(col, float3(1, 1, 1), cov(dt2) * (ok ? 1.0 : 0.35));
                }
                // chấm trang
                for (int j = 0; j < 16; j++)
                {
                    if (j >= (int)(pages + 0.5)) break;
                    float2 q = p - float2((j - (pages - 1.0) * 0.5) * 0.28, -8.5);
                    float d = length(q) - (abs(j - page) < 0.5 ? 0.12 : 0.075);
                    col = lerp(col, abs(j - page) < 0.5 ? gold : float3(0.5, 0.55, 0.8), cov(d));
                }
                // nút đóng
                {
                    float2 q = p - float2(hx - 1.0, vy - 1.0);
                    float d = length(q) - 0.58;
                    col = lerp(col, float3(0.06, 0.08, 0.17), cov(d));
                    col = lerp(col, gold, cov(abs(d + 0.04) - 0.045));
                    float dx = min(sdSeg(q, float2(-0.2, -0.2), float2(0.2, 0.2)), sdSeg(q, float2(-0.2, 0.2), float2(0.2, -0.2))) - 0.06;
                    col = lerp(col, float3(1, 1, 1), cov(dx));
                }
                return col;
            }

            float3 Overlay(float3 col, float2 p)
            {
                float st = _PP_Hud.w;
                if (st < 0.5) return col;
                if (st > 3.5) return SelectScreen(col, p);
                if (st > 2.5) return Menu(col, p);

                float t = _PP_Hud2.x;
                bool win = st < 1.5;
                col *= 1.0 - 0.6 * saturate(t / 0.3);

                float s = saturate(t / 0.45);
                float pop = 1.0 + 1.7 * pow(s - 1.0, 3.0) + 0.7 * pow(s - 1.0, 2.0);
                float2 pc = float2(0.0, 0.6);
                float2 q = (p - pc) / max(pop, 0.001);

                float3 edge = win ? float3(0.40, 0.90, 0.50) : float3(1.0, 0.38, 0.30);
                float d = sdBox(q, float2(2.9, 2.9) - 0.6) - 0.6;
                col *= 1.0 - 0.5 * exp(-max(d * pop, 0.0) * 1.8);
                col = lerp(col, float3(0.05, 0.07, 0.17), cov(d * pop));
                col = lerp(col, edge, cov((abs(d + 0.07) - 0.07) * pop));
                float shine = smoothstep(2.9, 1.0, q.y) * 0.05;
                col += shine * cov(d * pop);

                if (win)
                {
                    float3 starCol = float3(1.0, 0.82, 0.25);
                    for (int i = 0; i < 3; i++)
                    {
                        float2 sp = float2((i - 1) * 1.45, i == 1 ? 1.95 : 1.5);
                        float ap = saturate((t - 0.35 - 0.22 * i) / 0.3);
                        float sc = (1.0 + 1.5 * pow(ap - 1.0, 3.0) + 0.6 * pow(ap - 1.0, 2.0)) * (i == 1 ? 0.78 : 0.62);
                        float2 sq = (q - sp);
                        sq = rot2(sq, (i - 1) * -0.18) / max(sc, 0.001);
                        float ds = sdStar5(sq, 1.0, 0.45) * sc - 0.04;
                        col = lerp(col, float3(0.20, 0.24, 0.36), cov(ds + 0.0) * (1.0 - step(0.001, ap)));
                        col = lerp(col, starCol, cov(ds) * step(0.001, ap));
                        col += 0.25 * cov(ds) * step(0.001, ap) * smoothstep(0.1, -0.5, sq.y);
                    }
                    // dấu tích
                    float2 cq = q - float2(0.0, 0.22);
                    float dc = length(cq) - 0.68;
                    col = lerp(col, float3(0.20, 0.72, 0.38), cov(dc));
                    float dk = min(sdSeg(cq, float2(-0.30, -0.02), float2(-0.09, -0.23)),
                                   sdSeg(cq, float2(-0.09, -0.23), float2(0.31, 0.23))) - 0.085;
                    col = lerp(col, float3(1, 1, 1), cov(dk));
                }
                else
                {
                    float2 cq = q - float2(0.0, 1.2);
                    float dc = length(cq) - 1.05;
                    col = lerp(col, float3(0.85, 0.25, 0.22), cov(dc));
                    col = lerp(col, float3(1.0, 0.55, 0.45), cov(abs(dc + 0.06) - 0.03));
                    float dx = min(sdSeg(cq, float2(-0.4, -0.4), float2(0.4, 0.4)),
                                   sdSeg(cq, float2(-0.4, 0.4), float2(0.4, -0.4))) - 0.11;
                    col = lerp(col, float3(1, 1, 1), cov(dx));
                }

                // nút bấm
                {
                    float2 bq = q - float2(0.0, -1.3);
                    float bd = sdBox(bq, float2(1.5, 0.62) - 0.62) - 0.62;
                    float press = _PP_Hud2.w > 0.5 ? 0.92 : 1.0;
                    bq /= press;
                    bd = sdBox(bq, float2(1.5, 0.62) - 0.62) - 0.62;
                    float3 bc = win ? float3(0.25, 0.78, 0.40) : float3(1.0, 0.58, 0.20);
                    col = lerp(col, bc * 0.6, cov(bd + 0.0));
                    float2 sh = bq - float2(0.0, 0.06);
                    float bd2 = sdBox(sh, float2(1.5, 0.62) - 0.62) - 0.62;
                    col = lerp(col, bc, cov(bd2 + 0.04));
                    col += 0.18 * cov(bd2 + 0.04) * smoothstep(0.1, 0.5, bq.y);
                    if (win)
                    {
                        float dt2 = sdTri(bq, float2(-0.22, -0.34), float2(-0.22, 0.34), float2(0.4, 0.0)) - 0.06;
                        col = lerp(col, float3(1, 1, 1), cov(dt2));
                    }
                    else
                    {
                        float R = 0.26;
                        float a = atan2(bq.y, bq.x);
                        float ring = abs(length(bq) - R) - 0.06;
                        ring = max(ring, 0.1 - abs(a - radians(48.0)) * R * 1.6);
                        float2 P = R * float2(cos(radians(70.0)), sin(radians(70.0)));
                        float2 tg = float2(sin(radians(70.0)), -cos(radians(70.0)));
                        float2 nr = float2(cos(radians(70.0)), sin(radians(70.0)));
                        float dt2 = sdTri(bq, P + tg * 0.24, P + nr * 0.16, P - nr * 0.16);
                        col = lerp(col, float3(1, 1, 1), cov(min(ring, dt2)));
                    }
                }
                return col;
            }

            // ------------------------------------------------------------------ khung hình
            float4 frag(Vary i) : SV_Target
            {
                g_px = max(2.0 * _PP_View.y / _ScreenParams.y, 1e-4);
                float t = _PP_Hud2.y;
                float2 p = i.w + _PP_View.zw;

                float3 col = Background(p, t);
                const float3 LD = float3(-0.45, 0.65, 0.75);
                float3 L3 = normalize(LD);
                float2 L2 = normalize(float2(-0.5, 0.85));

                int n = (int)(_PP_Count.x + 0.5);

                [loop]
                for (int k = 0; k < n; k++)
                {
                    float4 A = _PP_A[k];
                    float4 B = _PP_B[k];
                    float4 C = _PP_C[k];
                    int kind = (int)(A.x + 0.5);
                    float2 q = p - A.yz;
                    float bound = length(B.xy) + 1.6;
                    if (dot(q, q) > bound * bound) continue;

                    // ---------- tường: tấm thép đóng đinh tán
                    if (kind == 0)
                    {
                        float2 l = rot2(q, -A.w);
                        float rr = B.z;
                        float d = sdBox(l, B.xy - rr) - rr;
                        col *= 1.0 - 0.5 * exp(-max(d, 0.0) * 4.5);
                        bool alongX = B.x >= B.y;
                        float2 uv = alongX ? l : l.yx;
                        float2 hh = alongX ? B.xy : B.yx;
                        float plate = 1.1;
                        float un = (uv.x + 200.0) / plate;
                        float cu = floor(un);
                        float fu = frac(un);
                        float rnd = hash21(float2(cu, hh.x * 3.1 + hh.y));
                        float v = uv.y / hh.y;
                        float3 c = lerp(float3(0.27, 0.31, 0.58), float3(0.42, 0.47, 0.78), saturate(0.5 + 0.5 * (alongX ? -v : v)));
                        c *= 0.92 + 0.14 * rnd;
                        c *= 0.95 + 0.07 * vnoise(p * 38.0);
                        float dSeam = min(fu, 1.0 - fu) * plate;
                        c = lerp(c, float3(0.09, 0.08, 0.22), (1.0 - smoothstep(0.0, 0.04, dSeam)) * 0.85);
                        c += float3(0.7, 0.75, 1.0) * 0.10 * smoothstep(0.09, 0.04, abs(dSeam - 0.07));
                        float du = (fu < 0.5 ? fu : fu - 1.0) * plate;
                        float vr = hh.y > 0.3 ? hh.y - 0.16 : 0.0;
                        float rd = length(float2(du, vr > 0.0 ? abs(uv.y) - vr : uv.y)) - 0.055;
                        c = lerp(c, float3(0.12, 0.11, 0.28), cov(rd - 0.02));
                        c = lerp(c, float3(0.62, 0.68, 0.98), cov(rd));
                        c += float3(1, 1, 1) * 0.35 * cov(length(float2(du, (vr > 0.0 ? abs(uv.y) - vr : uv.y)) - float2(-0.018, 0.018)) - 0.016);
                        if (hh.y > 0.25 && hh.x > 0.9)
                        {
                            float led = cov(abs(uv.y) - 0.016);
                            float flow = 0.5 + 0.5 * sin(uv.x * 3.0 - t * 3.0 + rnd * 6.0);
                            c += float3(0.2, 0.9, 1.0) * led * flow * 0.55;
                        }
                        float2 g = abs(l) - B.xy;
                        float2 nl = g.x > g.y ? float2(l.x < 0.0 ? -1.0 : 1.0, 0.0) : float2(0.0, l.y < 0.0 ? -1.0 : 1.0);
                        float lit = dot(rot2(nl, A.w), L2);
                        float eg = smoothstep(-0.11, -0.015, d);
                        c += eg * lit * float3(0.8, 0.85, 1.0) * 0.5;
                        c *= 1.0 - eg * 0.28 * saturate(1.0 - lit);
                        c = lerp(c, float3(0.05, 0.04, 0.13), smoothstep(-0.05, -0.008, d));
                        col = lerp(col, c, cov(d));
                    }
                    // ---------- thân chốt: ống thép có đai vàng
                    else if (kind == 1)
                    {
                        float2 l = rot2(q, -A.w);
                        float rr = B.y;
                        float2 sq = rot2(q - float2(0.07, -0.10), -A.w);
                        col *= 1.0 - 0.45 * cov(sdBox(sq, B.xy - rr) - rr + 0.03);
                        float d = sdBox(l, B.xy - rr) - rr;
                        float u = clamp(l.y / B.y, -1.0, 1.0);
                        float side = dot(rot2(float2(0, 1), A.w), L2) >= 0.0 ? 1.0 : -1.0;
                        u *= side;
                        float cyl = sqrt(max(1.0 - u * u, 0.0));
                        float3 c = lerp(float3(0.20, 0.25, 0.38), float3(0.78, 0.85, 0.98), cyl);
                        c += float3(1, 1, 1) * pow(saturate(1.0 - abs(u + 0.5) * 2.4), 2.5) * 0.8;
                        c += float3(0.5, 0.7, 1.0) * pow(saturate(1.0 - abs(u - 0.65) * 4.0), 2.0) * 0.25;
                        c *= 0.96 + 0.04 * sin(l.x * 90.0);
                        float tx = B.x - l.x;
                        float m = abs(tx - 1.7 * round((tx - 0.42) / 1.7) - 0.42);
                        float band = smoothstep(0.105, 0.085, m) * step(0.2, tx);
                        float3 gold = lerp(float3(0.5, 0.33, 0.05), float3(1.0, 0.85, 0.35), cyl) + float3(1, 0.95, 0.7) * pow(saturate(1.0 - abs(u + 0.5) * 2.4), 2.5) * 0.7;
                        c = lerp(c, gold, band);
                        c *= 1.0 - 0.45 * smoothstep(0.02, 0.0, abs(m - 0.105)) * step(0.2, tx);
                        float tip = smoothstep(0.34, 0.28, B.x + l.x);
                        c = lerp(c, gold * 0.9, tip);
                        c = lerp(c, float3(0.06, 0.06, 0.14), smoothstep(-0.04, -0.006, d));
                        col = lerp(col, c, cov(d));
                    }
                    // ---------- núm kéo vàng có đá quý
                    else if (kind == 2)
                    {
                        float2 l = rot2(q, -A.w);
                        float R = B.x;
                        float dk = length(l) - R;
                        float pulse = 0.5 + 0.5 * sin(_PP_Hud2.y * 4.0 + A.y);
                        // sóng lan báo hiệu có thể chạm
                        float rp = frac(_PP_Hud2.y * 0.9 + A.y * 0.13);
                        col += float3(1.0, 0.85, 0.4) * exp(-pow((length(q) - (R + 0.04 + rp * 0.75)) / 0.06, 2.0)) * (1.0 - rp) * 0.55 * B.z;
                        col += float3(1.0, 0.78, 0.30) * exp(-max(dk, 0.0) * 6.0) * B.z * (0.25 + 0.4 * pulse);
                        float2 sq = q - float2(0.07, -0.10);
                        col *= 1.0 - 0.45 * cov(length(sq) - R + 0.02);
                        // đai nối với thân
                        float dcol = sdBox(l - float2(-R * 0.95, 0.0), float2(0.13, 0.21) - 0.04) - 0.04;
                        float uc = clamp(l.y / 0.21, -1.0, 1.0);
                        float3 cc = lerp(float3(0.45, 0.30, 0.05), float3(1.0, 0.84, 0.34), sqrt(max(1.0 - uc * uc, 0.0)));
                        cc = lerp(cc, float3(0.2, 0.11, 0.02), smoothstep(-0.035, -0.006, dcol));
                        col = lerp(col, cc, cov(dcol));
                        // núm
                        float2 nq = l / R;
                        float rr2 = length(nq);
                        float z = sqrt(max(1.0 - rr2 * rr2, 0.0));
                        float3 nn = float3(nq, z);
                        float diff = saturate(dot(nn, L3));
                        float3 H = normalize(L3 + float3(0, 0, 1));
                        float3 c = float3(0.62, 0.40, 0.06) * (0.5 + 0.9 * diff) + float3(1.0, 0.95, 0.7) * pow(saturate(dot(nn, H)), 30.0) * 0.9;
                        c = lerp(c, float3(1.0, 0.86, 0.36) * (0.55 + 0.6 * diff), smoothstep(0.55, 0.85, rr2) * 0.5);
                        float ang = atan2(l.y, l.x);
                        c *= 1.0 - 0.2 * smoothstep(0.82, 0.95, rr2) * (0.5 + 0.5 * sin(ang * 20.0));
                        c *= 1.0 - 0.45 * smoothstep(0.04, 0.0, abs(rr2 - 0.66));
                        c = lerp(c, float3(0.2, 0.11, 0.02), smoothstep(-0.04, -0.006, dk));
                        col = lerp(col, c, cov(dk));
                        // đá quý
                        float gr = R * 0.40;
                        float dg = length(l) - gr;
                        float3 gem = lerp(float3(0.40, 0.03, 0.30), float3(1.0, 0.30, 0.65), 0.5 + 0.5 * sin(ang * 3.0 + 1.0));
                        gem *= 0.65 + 0.5 * smoothstep(gr, 0.0, length(l - float2(-0.3, 0.3) * gr));
                        gem += float3(1, 1, 1) * smoothstep(0.35 * gr, 0.0, length(l - float2(-0.38, 0.38) * gr)) * 0.9;
                        gem += float3(1.0, 0.5, 0.8) * (0.15 + 0.2 * pulse) * B.z;
                        gem = lerp(gem, float3(0.2, 0.02, 0.12), smoothstep(-0.03, -0.005, dg));
                        col = lerp(col, gem, cov(dg));
                    }
                    // ---------- dung nham trong máng đá bazan
                    else if (kind == 3)
                    {
                        float2 l = rot2(q, -A.w);
                        float rr = 0.15;
                        float d = sdBox(l, B.xy - rr) - rr;
                        float di = sdBox(l - float2(0.0, 1.0), float2(B.x - 0.17, B.y + 1.0 - 0.17) - rr) - rr;
                        float topEdge = B.y - 0.16 - 0.05 * sin(p.x * 5.0 + t * 2.0) - 0.03 * sin(p.x * 11.0 - t * 3.1);
                        float flick = 0.85 + 0.15 * sin(t * 3.0 + p.x * 2.0);
                        col += float3(1.0, 0.34, 0.06) * exp(-max(d, 0.0) * 2.4) * 0.42 * flick * (1.0 - cov(d));
                        // khí nóng phía trên mặt dung nham
                        float above = l.y - topEdge;
                        col += float3(1.0, 0.45, 0.1) * exp(-max(above, 0.0) * 3.2) * 0.22 * flick * step(0.0, above) * cov(di);

                        float2 w = p * float2(1.3, 1.8) + float2(t * 0.20, -t * 0.28);
                        float n1 = fbm(w + fbm(w * 1.6 + t * 0.12) * 1.7);
                        float3 lav = lerp(float3(0.55, 0.05, 0.0), float3(1.0, 0.38, 0.03), smoothstep(0.22, 0.6, n1));
                        lav = lerp(lav, float3(1.0, 0.82, 0.25), smoothstep(0.58, 0.85, n1));
                        float crust = smoothstep(0.58, 0.72, fbm(p * 2.1 + float2(t * 0.05, 3.0)));
                        lav = lerp(lav, float3(0.30, 0.04, 0.02), crust * 0.7);
                        float crack = smoothstep(0.05, 0.0, abs(fbm(p * 2.1 + float2(t * 0.05, 3.0)) - 0.56));
                        lav += float3(1.0, 0.7, 0.2) * crack * 0.35;
                        lav *= 0.9 + 0.1 * vnoise(p * 12.0 + t);
                        lav += float3(1.0, 0.85, 0.35) * exp(-abs(above) * 28.0) * 0.9;
                        float depth = saturate((topEdge - l.y) / 1.2);
                        lav *= 1.0 - 0.35 * depth;
                        float covL = cov(max(di, above));
                        col = lerp(col, lav, covL);

                        // máng đá
                        float3 st = lerp(float3(0.16, 0.12, 0.17), float3(0.26, 0.2, 0.26), vnoise(p * 7.0));
                        st += float3(1.0, 0.4, 0.08) * exp(-max(di, 0.0) * 9.0) * 0.5;
                        st = lerp(st, float3(0.05, 0.03, 0.06), smoothstep(-0.05, -0.008, d));
                        float lit = saturate(dot(normalize(float2(l.x < 0.0 ? -0.2 : 0.2, 1.0)), L2));
                        st += 0.18 * lit * smoothstep(-0.12, -0.02, d);
                        col = lerp(col, st, cov(d) * (1.0 - cov(di)));

                        // tàn lửa bay lên
                        float2 ge = p * float2(2.4, 1.5) + float2(0.0, -t * 0.55);
                        float2 ide = floor(ge);
                        float he = hash21(ide);
                        float em = smoothstep(0.07, 0.0, length(frac(ge) - 0.5 - (he - 0.5) * 0.5)) * step(0.86, he);
                        col += float3(1.0, 0.65, 0.2) * em * 0.9 * step(0.0, above) * exp(-max(above, 0.0) * 1.6) * cov(di);
                    }
                    // ---------- bom
                    else if (kind == 4)
                    {
                        float r = B.x;
                        float d = length(q) - r;
                        col *= 1.0 - 0.40 * cov(length(q - float2(0.08, -0.10)) - r + 0.03);
                        col *= 1.0 - 0.3 * exp(-max(d, 0.0) * 10.0);
                        float2 l = rot2(q, -A.w);

                        // nắp + ngòi
                        float dcap = sdBox(l - float2(0.0, r * 0.98), float2(r * 0.24, r * 0.14)) - 0.02;
                        float dfuse = sdSeg(l, float2(0.0, r * 1.05), float2(r * 0.40, r * 1.50)) - 0.035;
                        float3 body = float3(0.42, 0.34, 0.2);
                        col = lerp(col, float3(0.30, 0.32, 0.38), cov(dcap));
                        col = lerp(col, body, cov(dfuse));
                        float2 spk = float2(r * 0.40, r * 1.52);
                        float fl = 0.75 + 0.25 * sin(t * 31.0 + A.y * 3.0);
                        float ds = length(l - spk);
                        col += float3(1.0, 0.62, 0.15) * exp(-ds * 9.0) * fl * 1.1;
                        col = lerp(col, float3(1.0, 0.95, 0.6), cov(ds - 0.05 * fl));

                        // thân
                        float2 nq = q / r;
                        float z = sqrt(max(1.0 - dot(nq, nq), 0.0));
                        float3 nn = float3(nq, z);
                        float diff = saturate(dot(nn, L3));
                        float3 H = normalize(L3 + float3(0, 0, 1));
                        float spec = pow(saturate(dot(nn, H)), 38.0);
                        float rim = pow(1.0 - z, 3.0);
                        float3 c = float3(0.10, 0.11, 0.16) * (0.4 + 1.2 * diff) + spec * 0.8 + rim * float3(0.5, 0.15, 0.1) * 0.5;
                        // huy hiệu nguy hiểm
                        float pulse = 0.65 + 0.35 * sin(t * 7.0 + A.y);
                        float db = length(l) - r * 0.42;
                        float3 red = float3(1.0, 0.18, 0.12) * (0.55 + 0.7 * pulse);
                        c = lerp(c, red, smoothstep(0.02, -0.02, db));
                        float dbar = sdBox(l - float2(0.0, r * 0.07), float2(r * 0.065, r * 0.18)) - 0.01;
                        float ddot = length(l - float2(0.0, -r * 0.19)) - r * 0.075;
                        c = lerp(c, float3(1, 1, 1), smoothstep(0.015, -0.015, min(dbar, ddot)));
                        col += float3(1.0, 0.2, 0.1) * exp(-max(d, 0.0) * 6.0) * 0.18 * pulse;
                        c = lerp(c, float3(0.02, 0.02, 0.06), smoothstep(-0.05, -0.006, d));
                        col = lerp(col, c, cov(d));
                    }
                    // ---------- bóng
                    else if (kind == 5)
                    {
                        float r = B.x;
                        float d = length(q) - r;
                        col *= 1.0 - 0.38 * cov(length(q - float2(0.05, -0.07)) - r + 0.02);
                        col *= 1.0 - 0.25 * exp(-max(d, 0.0) * 14.0);
                        float2 nq = q / r;
                        float z = sqrt(max(1.0 - dot(nq, nq), 0.0));
                        float3 nn = float3(nq, z);
                        float diff = saturate(dot(nn, L3));
                        float3 H = normalize(L3 + float3(0, 0, 1));
                        float spec = pow(saturate(dot(nn, H)), 34.0);
                        float rim = pow(1.0 - z, 3.0);
                        float3 base = C.rgb;
                        float3 c = base * (0.55 + 0.75 * diff) + spec * 1.0 + rim * base * 0.45 + float3(0.9, 0.95, 1.0) * pow(saturate(dot(nn, normalize(float3(0.5, -0.6, 0.6)))), 6.0) * 0.12;
                        c += pow(saturate(1.0 - length(nq - float2(-0.35, 0.4)) * 3.2), 2.0) * 0.55;
                        float2 sp = rot2(q, -A.w) / r;
                        float spot = smoothstep(0.30, 0.12, length(sp - float2(0.52, 0.0)));
                        c *= 1.0 - 0.17 * spot;
                        c = lerp(c, float3(1, 1, 1), saturate(B.z) * 0.45);
                        col += base * exp(-max(d, 0.0) * 5.0) * saturate(B.z) * 0.22;
                        c = lerp(c, base * 0.22, smoothstep(-0.045, -0.005, d));
                        col = lerp(col, c, cov(d));
                    }
                    // ---------- kính bình
                    else if (kind == 6)
                    {
                        float2 l = rot2(q, -A.w);
                        float rr = B.z;
                        float d = sdBox(l, B.xy - rr) - rr;
                        bool alongX = B.x >= B.y;
                        float sA = alongX ? l.y / B.y : l.x / B.x;
                        float3 glass = float3(0.62, 0.90, 1.0);
                        float3 c = lerp(col * 0.85 + glass * 0.22, glass, 0.38 + 0.25 * sA * sA);
                        c += float3(1, 1, 1) * 0.65 * smoothstep(0.38, 0.0, abs(sA + 0.4)) * 0.6;
                        c += float3(0.7, 0.9, 1.0) * 0.18 * smoothstep(0.3, 0.0, abs(sA - 0.55));
                        float run = alongX ? l.x : l.y;
                        c += float3(1, 1, 1) * 0.35 * smoothstep(0.05, 0.0, abs(frac(run * 0.5 + t * 0.07) - 0.5) - 0.46);
                        c = lerp(c, float3(0.9, 0.98, 1.0), 0.55 * smoothstep(-0.05, -0.008, d));
                        c = lerp(c, float3(0.12, 0.40, 0.58), 0.7 * smoothstep(-0.02, 0.0, d));
                        col = lerp(col, c, cov(d) * 0.88);
                    }
                    // ---------- lòng bình (+ mũi tên mục tiêu)
                    else if (kind == 7)
                    {
                        float2 l = rot2(q, -A.w);
                        float d = sdBox(l, B.xy);
                        float3 tint = C.rgb;
                        float tintAmt = C.a;
                        float3 inner = col * 0.5 + tint * (0.10 + 0.22 * tintAmt);
                        inner += tint * 0.25 * smoothstep(0.0, B.y * 2.0, B.y - l.y) * tintAmt;
                        inner += float3(0.12, 0.2, 0.35) * smoothstep(-B.y, B.y, -l.y) * 0.25;
                        col = lerp(col, inner, cov(d));

                        float shaft = smoothstep(0.22, 0.0, abs(frac(l.x * 0.45 + l.y * 0.3 + t * 0.04) - 0.5) - 0.3);
                        col += tint * 0.05 * shaft * cov(d) + float3(0.8, 0.9, 1.0) * 0.04 * shaft * cov(d);
                        float2 gs = l * 2.2 + float2(0.0, -t * 0.25);
                        float2 ids = floor(gs);
                        float hs = hash21(ids + 9.0);
                        float tw = smoothstep(0.08, 0.0, length(frac(gs) - 0.5 - (hs - 0.5) * 0.6)) * step(0.8, hs) * (0.5 + 0.5 * sin(t * 4.0 + hs * 20.0));
                        col += float3(1, 1, 1) * tw * 0.6 * cov(d);

                        // mũi tên nhấp nháy phía trên miệng bình
                        float2 u = q - float2(0.0, B.y + 1.25 + 0.15 * sin(t * 4.0));
                        float chev = abs(u.y + abs(u.x) * 0.95) - 0.065;
                        chev = max(chev, abs(u.x) - 0.42);
                        float3 ac = lerp(float3(0.9, 0.95, 1.0), tint, tintAmt);
                        col = lerp(col, ac, cov(chev) * (0.6 + 0.3 * sin(t * 4.0)));
                    }
// ---------- gai thép
                    else if (kind == 10)
                    {
                        float2 l = rot2(q, -A.w);
                        float d = sdBox(l, B.xy);
                        col += float3(1.0, 0.15, 0.12) * exp(-max(d, 0.0) * 5.0) * 0.14 * (1.0 - cov(d));
                        float rowH = min(B.y * 2.0, 0.9);
                        float v = l.y + B.y;
                        float row = floor(v / rowH);
                        float fv = v - row * rowH;
                        float tw = 0.34;
                        float uu = frac((l.x + 80.0) / tw);
                        float tri = 1.0 - abs(2.0 * uu - 1.0);
                        float dtooth = (fv - tri * rowH) * 0.62;
                        float3 bgc = float3(0.07, 0.05, 0.09) + float3(0.25, 0.04, 0.04) * (1.0 - fv / rowH) * 0.4;
                        col = lerp(col, bgc, cov(d));
                        float shade = uu < 0.5 ? 1.0 : 0.5;
                        float3 tc = float3(0.60, 0.65, 0.80) * shade * (0.65 + 0.35 * fv / rowH);
                        float tipk = pow(saturate(fv / max(tri * rowH, 1e-3)), 6.0);
                        tc += float3(0.7, 0.08, 0.06) * tipk * 0.8;
                        tc = lerp(tc, float3(0.05, 0.04, 0.1), smoothstep(-0.03, -0.004, dtooth));
                        col = lerp(col, tc, cov(max(d, dtooth)));
                    }
                    // ---------- cưa quay
                    else if (kind == 11)
                    {
                        float R = B.x;
                        float dd = length(q);
                        float ang = atan2(q.y, q.x) - A.w;
                        float kk = frac(ang * 14.0 / 6.2831853 + 100.0);
                        float d = dd - R * (0.84 + 0.16 * kk);
                        col *= 1.0 - 0.4 * cov(length(q - float2(0.07, -0.10)) - R * 0.9);
                        col += float3(1.0, 0.18, 0.12) * exp(-max(d, 0.0) * 5.0) * 0.20 * (0.7 + 0.3 * sin(_PP_Hud2.y * 6.0));
                        float3 c = lerp(float3(0.42, 0.46, 0.58), float3(0.88, 0.92, 1.0), saturate(0.5 + 0.5 * dot(q / R, L2)));
                        c *= 0.94 + 0.06 * sin(dd * 70.0);
                        c += 0.25 * pow(saturate(0.5 + 0.5 * sin(ang * 3.0)), 6.0) * smoothstep(0.3 * R, R, dd);
                        float2 hq = rot2(q, -A.w);
                        float holes = 1e3;
                        for (int hI = 0; hI < 3; hI++)
                        {
                            float a = hI * 2.0943951;
                            holes = min(holes, length(hq - R * 0.58 * float2(cos(a), sin(a))) - R * 0.14);
                        }
                        c = lerp(c, float3(0.07, 0.05, 0.1), smoothstep(0.02, -0.01, holes));
                        float dh = dd - R * 0.26;
                        c = lerp(c, lerp(float3(0.5, 0.33, 0.06), float3(1.0, 0.85, 0.35), saturate(0.5 + 0.5 * dot(q / (R * 0.26), L2))), smoothstep(0.015, -0.015, dh));
                        c = lerp(c, float3(0.35, 0.07, 0.07), smoothstep(0.82, 1.0, kk) * smoothstep(0.55 * R, R, dd) * 0.8);
                        c = lerp(c, float3(0.05, 0.04, 0.1), smoothstep(-0.045, -0.006, d));
                        col = lerp(col, c, cov(d));
                    }
                    // ---------- hàng rào điện
                    else if (kind == 12)
                    {
                        float2 l = rot2(q, -A.w);
                        float hx = B.x;
                        float st = B.z;
                        float pr = 0.22;
                        float3 zc = float3(0.35, 0.9, 1.0);
                        float inb = step(abs(l.x), hx + 0.12);
                        if (st > 1.5)
                        {
                            float jag = (vnoise(float2(l.x * 6.0, t * 30.0)) - 0.5) * 0.36 + (vnoise(float2(l.x * 13.0 + 7.0, t * 45.0)) - 0.5) * 0.16;
                            float dl = abs(l.y - jag);
                            col += zc * exp(-dl * 26.0) * 1.3 * inb;
                            col += float3(1, 1, 1) * exp(-dl * 95.0) * 1.2 * inb;
                            col += zc * 0.16 * cov(sdBox(l, float2(hx, B.y + 0.12))) * (0.7 + 0.3 * sin(t * 60.0));
                        }
                        else if (st > 0.5)
                        {
                            float dash = step(0.5, frac(l.x * 4.0 + t * 2.0));
                            col += float3(1.0, 0.75, 0.2) * exp(-abs(l.y) * 40.0) * dash * inb * (0.35 + 0.35 * sin(t * 26.0));
                        }
                        else
                        {
                            float dash = step(0.7, frac(l.x * 3.0));
                            col += zc * 0.18 * exp(-abs(l.y) * 60.0) * dash * inb;
                        }
                        for (int pI = 0; pI < 2; pI++)
                        {
                            float2 pc = float2(pI == 0 ? -hx - 0.12 : hx + 0.12, 0.0);
                            float2 pq = l - pc;
                            float dp = length(pq) - pr;
                            col *= 1.0 - 0.4 * cov(length(pq - float2(0.06, -0.08)) - pr);
                            float3 pcol = lerp(float3(0.14, 0.16, 0.26), float3(0.55, 0.6, 0.78), saturate(0.5 + 0.5 * dot(pq / pr, L2)));
                            pcol = lerp(pcol, float3(0.05, 0.04, 0.1), smoothstep(-0.04, -0.005, dp));
                            col = lerp(col, pcol, cov(dp));
                            float3 core = st > 1.5 ? float3(0.9, 1.0, 1.0) : (st > 0.5 ? float3(1.0, 0.7, 0.15) : float3(0.1, 0.35, 0.4));
                            float dc = length(pq) - pr * 0.45;
                            col += core * exp(-max(dc, 0.0) * 14.0) * (st > 1.5 ? 0.9 : 0.35);
                            col = lerp(col, core, cov(dc) * 0.95);
                        }
                    }
                    // ---------- bóng gai
                    else if (kind == 13)
                    {
                        float R = B.x;
                        float dd = length(q);
                        float ang = atan2(q.y, q.x) - A.w;
                        float sp = pow(abs(cos(ang * 4.0)), 10.0);
                        float d = dd - R * (0.62 + 0.38 * sp);
                        col *= 1.0 - 0.4 * cov(length(q - float2(0.07, -0.10)) - R * 0.8);
                        float pulse = 0.65 + 0.35 * sin(_PP_Hud2.y * 7.0 + A.y);
                        col += float3(1.0, 0.15, 0.2) * exp(-max(d, 0.0) * 6.0) * 0.2 * pulse;
                        float2 nq = q / (R * 0.62);
                        float z = sqrt(max(1.0 - dot(nq, nq), 0.0));
                        float3 nn = float3(nq, z);
                        float diff = saturate(dot(nn, L3));
                        float3 H = normalize(L3 + float3(0, 0, 1));
                        float3 c = float3(0.30, 0.32, 0.42) * (0.4 + 1.1 * diff) + pow(saturate(dot(nn, H)), 30.0) * 0.8;
                        float spk = smoothstep(0.58 * R, 0.95 * R, dd);
                        c = lerp(c, float3(0.75, 0.80, 0.95) * (0.6 + 0.4 * saturate(dot(normalize(q), L2))), spk * step(0.64 * R, dd));
                        c = lerp(c, float3(0.9, 0.12, 0.15), smoothstep(0.86 * R, 1.0 * R, dd) * 0.9);
                        float2 rq = rot2(q, -A.w);
                        float eye = length(rq) - R * 0.2;
                        c = lerp(c, float3(1.0, 0.12, 0.15) * (0.6 + 0.8 * pulse), smoothstep(0.02, -0.02, eye));
                        c = lerp(c, float3(0.04, 0.03, 0.08), smoothstep(-0.04, -0.006, d));
                        col = lerp(col, c, cov(d));
                    }
                    // ---------- cọc đẩy bóng
                    else if (kind == 14)
                    {
                        float R = B.x;
                        float d = length(q) - R;
                        col *= 1.0 - 0.4 * cov(length(q - float2(0.05, -0.07)) - R);
                        float2 nq = q / R;
                        float z = sqrt(max(1.0 - dot(nq, nq), 0.0));
                        float3 nn = float3(nq, z);
                        float diff = saturate(dot(nn, L3));
                        float3 H = normalize(L3 + float3(0, 0, 1));
                        float3 c = float3(0.78, 0.82, 0.95) * (0.5 + 0.7 * diff) + pow(saturate(dot(nn, H)), 24.0) * 0.9;
                        float rg = length(q) / R;
                        c = lerp(c, float3(1.0, 0.8, 0.3) * (0.6 + 0.5 * diff), smoothstep(0.08, 0.0, abs(rg - 0.62)));
                        c = lerp(c, float3(0.25, 0.28, 0.45), smoothstep(0.35, 0.2, rg));
                        c = lerp(c, float3(0.05, 0.05, 0.12), smoothstep(-0.05, -0.006, d));
                        col = lerp(col, c, cov(d));
                    }
                    // ---------- tia lửa
                    else if (kind == 8)
                    {
                        float k0 = saturate(1.0 - length(q) / B.x);
                        col += C.rgb * C.a * (k0 * k0 * 1.0);
                    }
                    // ---------- sóng xung kích
                    else if (kind == 9)
                    {
                        float r = length(q);
                        float v = (r - B.x) / max(B.y, 1e-3);
                        col += C.rgb * C.a * exp(-v * v);
                    }
                }

                col = Lamps(col, p, t);
                if (_PP_Hud.w < 2.5) col = Hud(col, i.w, t);
                col = Overlay(col, i.w);
                col *= 1.0 - saturate(_PP_Hud2.z);
                col += (hash21(i.pos.xy) - 0.5) / 255.0;
                // màu trong shader được chọn theo cảm nhận sRGB; đổi sang tuyến tính nếu project dùng Linear
                #if !defined(UNITY_COLORSPACE_GAMMA)
                col = pow(max(col, 0.0), 2.2);
                #endif
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
