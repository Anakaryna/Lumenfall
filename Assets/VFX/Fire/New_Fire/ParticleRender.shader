// Renders GPU particles as camera-facing billboards.
// Input is a StructuredBuffer<Particle> indexed by SV_InstanceID.
// Geometry shader expands each particle into a quad; fragment draws a soft circle.

Shader "Custom/ParticleRender" 
{
    Properties
    {
        _BillboardSize ("Billboard Size (world)", Float) = 0.035
        _Softness ("Edge Softness", Range(0.001, 0.5)) = 0.08
        _Color ("Color", Color) = (1,1,1,1)
        _UseHeightGradient ("Use Height Gradient (0/1)", Float) = 0
        _GradientMinY ("Gradient Min Y", Float) = 0
        _GradientMaxY ("Gradient Max Y", Float) = 3
        _LowColor ("Low Color", Color) = (0,0.6,1,1)
        _HighColor ("High Color", Color) = (1,0.2,0.2,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            
            #pragma target 5.0
            #pragma require geometry
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"

            float _BillboardSize;
            float _Softness;
            float4 _Color;
            float _UseHeightGradient;
            float _GradientMinY;
            float _GradientMaxY;
            float4 _LowColor;
            float4 _HighColor;

            struct Particle
            {
                float3 pos;
                float3 vel;
                float life;
                float pad;
            };

            StructuredBuffer<Particle> _Particles;

            struct appdata
            {
                uint instanceID : SV_InstanceID;
            };

            struct v2g
            {
                float4 posCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
                float  life  : TEXCOORD1;
            };

            struct g2f
            {
                float4 posCS : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float  life  : TEXCOORD1;
                float heightT : TEXCOORD2;
            };

            v2g vert(appdata v)
            {
                v2g o;
                Particle p = _Particles[v.instanceID];

                o.posWS = p.pos;
                o.life  = p.life;
                o.posCS = UnityWorldToClipPos(float4(p.pos, 1.0));
                return o;
            }

            [maxvertexcount(4)]
            void geom(point v2g IN[1], inout TriangleStream<g2f> triStream)
            {
                float life = IN[0].life;
                if (life <= 0.0) return;

                float3 center = IN[0].posWS;
                float denom = max(1e-5, (_GradientMaxY - _GradientMinY));
                float t = saturate((center.y - _GradientMinY) / denom);
                
                float3 camRight = UNITY_MATRIX_I_V._m00_m01_m02;
                float3 camUp    = UNITY_MATRIX_I_V._m10_m11_m12;

                float size = _BillboardSize;
                float3 right = camRight * size;
                float3 up    = camUp    * size;

                float3 p0 = center - right - up; // BL
                float3 p1 = center + right - up; // BR
                float3 p2 = center - right + up; // TL
                float3 p3 = center + right + up; // TR

                g2f o;
                o.life = life;

                o.heightT = t;
                o.uv = float2(0, 0); o.posCS = UnityWorldToClipPos(float4(p0, 1)); triStream.Append(o);
                o.heightT = t;
                o.uv = float2(1, 0); o.posCS = UnityWorldToClipPos(float4(p1, 1)); triStream.Append(o);
                o.heightT = t;
                o.uv = float2(0, 1); o.posCS = UnityWorldToClipPos(float4(p2, 1)); triStream.Append(o);
                o.heightT = t;
                o.uv = float2(1, 1); o.posCS = UnityWorldToClipPos(float4(p3, 1)); triStream.Append(o);

                triStream.RestartStrip();
            }

            half4 frag(g2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r2 = dot(p, p);

                // circle cut
                clip(1.0 - r2);

                float r = sqrt(r2);
                float a = 1.0 - smoothstep(1.0 - _Softness, 1.0, r);
                a *= saturate(i.life);

                float3 baseCol = _Color.rgb;

                if (_UseHeightGradient > 0.5)
                {
                    baseCol = lerp(_LowColor.rgb, _HighColor.rgb, i.heightT);
                }

                // premultiplied
                return half4(baseCol * a, a);
            }
            ENDHLSL
        }
    }
}