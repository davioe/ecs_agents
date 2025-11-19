Shader "Custom/InstancedIndirectLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo (RGB)", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags {"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Geometry"}

        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}

            HLSLPROGRAM
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // --- Instancing ---
            #pragma multi_compile_instancing
            #pragma multi_compile _ PROCEDURAL_INSTANCING_ON
            #pragma instancing_options procedural:InstancingSetup

            // --- Lighting Keywords ---
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _FORWARD_PLUS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            // NEUE STRUKTUR: 32 Byte (2x float4)
            struct InstanceData
            {
                float4 posScale; // xyz = Position, w = Scale
                float4 rot;      // xyzw = Quaternion
            };

            StructuredBuffer<InstanceData> _PerInstanceData;

            // Hilfsfunktion: Quaternion zu 3x3 Matrix
            float3x3 QuaternionToMatrix(float4 quat)
            {
                float x = quat.x, y = quat.y, z = quat.z, w = quat.w;
                float x2 = x + x, y2 = y + y, z2 = z + z;
                float xx = x * x2, xy = x * y2, xz = x * z2;
                float yy = y * y2, yz = y * z2, zz = z * z2;
                float wx = w * x2, wy = w * y2, wz = w * z2;

                return float3x3(
                    1.0 - (yy + zz), xy - wz, xz + wy,
                    xy + wz, 1.0 - (xx + zz), yz - wx,
                    xz - wy, yz + wx, 1.0 - (xx + yy)
                );
            }

            void InstancingSetup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    // Daten lesen
                    InstanceData data = _PerInstanceData[unity_InstanceID];

                    float3 position = data.posScale.xyz;
                    float scale = data.posScale.w;
                    float4 rotation = data.rot;

                    // 1. ObjectToWorld Matrix bauen
                    float3x3 rotMat = QuaternionToMatrix(rotation);
                    
                    // Skalierung anwenden
                    float3x3 trs = rotMat * scale;

                    unity_ObjectToWorld._11_21_31_41 = float4(trs._11, trs._21, trs._31, 0.0);
                    unity_ObjectToWorld._12_22_32_42 = float4(trs._12, trs._22, trs._32, 0.0);
                    unity_ObjectToWorld._13_23_33_43 = float4(trs._13, trs._23, trs._33, 0.0);
                    unity_ObjectToWorld._14_24_34_44 = float4(position.x, position.y, position.z, 1.0);

                    // 2. WorldToObject (Inverse) bauen
                    // Inverse einer TRS Matrix: M^-1 = S^-1 * R^-1 * T^-1
                    float invScale = 1.0 / scale;
                    float3x3 invRot = transpose(rotMat); // Für Rotationsmatrizen ist die Inverse gleich der Transponierten
                    float3x3 invTrs = invRot * invScale;
                    float3 invTrans = mul(invTrs, -position);

                    unity_WorldToObject._11_21_31_41 = float4(invTrs._11, invTrs._21, invTrs._31, 0.0);
                    unity_WorldToObject._12_22_32_42 = float4(invTrs._12, invTrs._22, invTrs._32, 0.0);
                    unity_WorldToObject._13_23_33_43 = float4(invTrs._13, invTrs._23, invTrs._33, 0.0);
                    unity_WorldToObject._14_24_34_44 = float4(invTrans.x, invTrans.y, invTrans.z, 1.0);
                #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            Varyings LitPassVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 LitPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = baseMap.rgb * _BaseColor.rgb;
                half alpha = baseMap.a * _BaseColor.a;
                
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowMask = half4(1,1,1,1); 

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.alpha = alpha;
                surfaceData.occlusion = 1.0h;
                surfaceData.emission = 0.0h;
                surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 1.0h;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                return color;
            }
            ENDHLSL
        }
    }
}