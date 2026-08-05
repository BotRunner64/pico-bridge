Shader "PicoBridge/StereoSBS"
{
    Properties
    {
        _MainTex ("Side-by-Side Texture", 2D) = "black" {}
        _SwapEyes ("Swap Eyes", Float) = 0
        _FlipY ("Flip Y", Float) = 0
        _SourceIntrinsics ("Normalized Source Intrinsics", Vector) = (0.5, 0.888889, 0.5, 0.5)
        _LeftEyeProjection ("Left Eye Projection", Vector) = (1, 0, 1, 0)
        _RightEyeProjection ("Right Eye Projection", Vector) = (1, 0, 1, 0)
        _EdgeFeather ("Edge Feather", Range(0, 0.1)) = 0.025
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _SwapEyes;
            float _FlipY;
            float4 _SourceIntrinsics;
            float4 _LeftEyeProjection;
            float4 _RightEyeProjection;
            float _EdgeFeather;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // The editor-authored quad is only a render hook. Pin its vertices
                // to clip space; the fragment stage performs calibrated per-eye mapping.
                output.vertex = float4(input.vertex.xy * 2.0, 0.0, 1.0);
                output.uv = input.uv;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float outputEye = unity_StereoEyeIndex;
                float sourceEye = outputEye;
                if (_SwapEyes > 0.5)
                    sourceEye = 1.0 - sourceEye;

                // Reconstruct the headset view ray, then project that ray with
                // the rectified source-camera intrinsics. This keeps angular
                // size and image proportions intact instead of stretching a
                // 16:9 camera image to the headset's per-eye viewport.
                float4 eyeProjection = outputEye < 0.5
                    ? _LeftEyeProjection
                    : _RightEyeProjection;
                float2 outputNdc = input.uv * 2.0 - 1.0;
                float2 tangentAngle = float2(
                    (outputNdc.x + eyeProjection.y) / eyeProjection.x,
                    (outputNdc.y + eyeProjection.w) / eyeProjection.z);

                float2 sourceUv = float2(
                    tangentAngle.x * _SourceIntrinsics.x + _SourceIntrinsics.z,
                    tangentAngle.y * _SourceIntrinsics.y + (1.0 - _SourceIntrinsics.w));

                float2 distanceToEdge = min(sourceUv, 1.0 - sourceUv);
                float nearestEdge = min(distanceToEdge.x, distanceToEdge.y);
                clip(nearestEdge);

                if (_FlipY > 0.5)
                    sourceUv.y = 1.0 - sourceUv.y;

                float2 textureUv = float2(
                    sourceUv.x * 0.5 + sourceEye * 0.5,
                    sourceUv.y);
                fixed4 color = tex2D(_MainTex, textureUv);
                float alpha = smoothstep(0.0, max(_EdgeFeather, 0.00001), nearestEdge);
                color.rgb *= alpha;
                color.a = alpha;

                return color;
            }
            ENDCG
        }
    }
}
