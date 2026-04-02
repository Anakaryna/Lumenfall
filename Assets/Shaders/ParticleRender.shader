Shader "Custom/ParticleRenderRainSnow"
{
    Properties
    {
        _DropWidth ("Drop Width", Float) = 0.012
        _DropLength ("Drop Length", Float) = 0.20
        _Color ("Color", Color) = (0.72,0.82,0.95,0.22)
        _AlphaBoost ("Alpha Boost", Float) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 5.0
            #pragma require geometry
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"

            float _DropWidth;
            float _DropLength;
            float4 _Color;
            float _AlphaBoost;

            struct Particle
            {
                float3 pos;
                float3 vel;
                float life;
                float type;
            };

            StructuredBuffer<Particle> _Particles;

            struct appdata
            {
                uint instanceID : SV_InstanceID;
            };

            struct v2g
            {
                float3 posWS : TEXCOORD0;
                float3 velWS : TEXCOORD1;
                float life : TEXCOORD2;
                float type : TEXCOORD3;
            };

            struct g2f
            {
                float4 posCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float life : TEXCOORD1;
                float3 velWS : TEXCOORD2;
                float type : TEXCOORD3;
            };

            v2g vert(appdata v)
            {
                v2g o;
                Particle p = _Particles[v.instanceID];
                o.posWS = p.pos;
                o.velWS = p.vel;
                o.life = p.life;
                o.type = p.type;
                return o;
            }

            [maxvertexcount(4)]
            void geom(point v2g IN[1], inout TriangleStream<g2f> triStream)
            {
                if (IN[0].life <= 0.0) return;

                float3 center = IN[0].posWS;
                float3 vel = IN[0].velWS;

                float3 camFwd = normalize(_WorldSpaceCameraPos.xyz - center);

                float width = _DropWidth;
                float lengthWS = _DropLength;

                float3 right;
                float3 upish;

                if (IN[0].type < 0.5)
                {
                    float speed = max(length(vel), 0.001);
                    float3 dir = normalize(vel);

                    right = normalize(cross(camFwd, dir));
                    if (dot(right, right) < 1e-5)
                        right = float3(1, 0, 0);

                    float lengthMul = saturate(speed / 18.0);
                    lengthWS = _DropLength * lerp(0.7, 1.6, lengthMul);

                    upish = dir;
                }
                else
                {
                    right = normalize(cross(camFwd, float3(0, 1, 0)));
                    if (dot(right, right) < 1e-5)
                        right = float3(1, 0, 0);

                    upish = float3(0, 1, 0);
                }

                float3 halfRight = right * width;
                float3 halfUp = upish * (lengthWS * 0.5);

                float3 p0 = center - halfRight - halfUp;
                float3 p1 = center + halfRight - halfUp;
                float3 p2 = center - halfRight + halfUp;
                float3 p3 = center + halfRight + halfUp;

                g2f o;
                o.life = IN[0].life;
                o.velWS = vel;
                o.type = IN[0].type;

                o.uv = float2(0, 0); o.posCS = UnityWorldToClipPos(float4(p0, 1)); triStream.Append(o);
                o.uv = float2(1, 0); o.posCS = UnityWorldToClipPos(float4(p1, 1)); triStream.Append(o);
                o.uv = float2(0, 1); o.posCS = UnityWorldToClipPos(float4(p2, 1)); triStream.Append(o);
                o.uv = float2(1, 1); o.posCS = UnityWorldToClipPos(float4(p3, 1)); triStream.Append(o);

                triStream.RestartStrip();
            }

            half4 frag(g2f i) : SV_Target
            {
                // thin raindrop streak
                float2 uv = i.uv;
                float alpha;
                float3 col = _Color.rgb;

                if (i.type < 0.5)
                {
                    float side = 1.0 - abs(uv.x * 2.0 - 1.0);
                    side = smoothstep(0.0, 0.8, side);

                    float along = sin(uv.y * 3.14159265);
                    along = saturate(along);

                    alpha = side * along;
                    col *= lerp(0.85, 1.15, side);
                }
                else
                {
                    float2 d = uv * 2.0 - 1.0;
                    float r = length(d);

                    alpha = 1.0 - smoothstep(0.55, 1.0, r);

                    float softCore = 1.0 - smoothstep(0.0, 0.65, r);
                    col *= lerp(0.95, 1.15, softCore);
                }

                alpha *= saturate(i.life * 2.0);
                alpha *= _Color.a * _AlphaBoost;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}