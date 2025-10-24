using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class FaceMouseSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // Main-thread: Maus-Ray berechnen
        var mousePos = Input.mousePosition;
        var ray = cam.ScreenPointToRay(mousePos);
        float3 rayOrigin = ray.origin;
        float3 rayDir = ray.direction;

        float dt = SystemAPI.Time.DeltaTime;

        // Job instanziieren und parallel schedulen
        var job = new FaceMouseJob
        {
            RayOrigin = rayOrigin,
            RayDir = rayDir,
            DeltaTime = dt
        };

        // Schedule parallel; der Job arbeitet auf LocalTransform (ref) & FaceMouseComponent (in)
        this.Dependency = job.ScheduleParallel(this.Dependency);
    }

    [BurstCompile]
    partial struct FaceMouseJob : IJobEntity
    {
        public float3 RayOrigin;
        public float3 RayDir;
        public float DeltaTime;

        // Execute wird für jede Entity mit (ref LocalTransform, in FaceMouseComponent) ausgeführt
        public void Execute(ref LocalTransform tf, in FaceMouseComponent fm)
        {
            const float eps = 1e-6f;

            // 1) Schnittpunkt Ray <-> Ebene y = PlaneY berechnen
            float3 targetWorld;
            if (math.abs(RayDir.y) > eps)
            {
                float t = (fm.PlaneY - RayOrigin.y) / RayDir.y;
                targetWorld = RayOrigin + RayDir * t;
            }
            else
            {
                // Ray nahezu parallel → projiziere Origin auf Ebene
                targetWorld = new float3(RayOrigin.x, fm.PlaneY, RayOrigin.z);
            }

            // 2) Richtung vom Entity zur Zielposition
            float3 toTarget = targetWorld - tf.Position;

            if (fm.LockPitch == 1)
            {
                toTarget.y = 0f;
                if (math.lengthsq(toTarget) < 1e-8f) return;
                float3 forward = math.normalize(toTarget);
                quaternion desired = quaternion.LookRotationSafe(forward, new float3(0f, 1f, 0f));

                if (fm.RotationSpeed <= 0f)
                {
                    tf.Rotation = desired;
                }
                else
                {
                    // Winkel zwischen Quaternions (in Grad)
                    float dot = math.dot(tf.Rotation.value, desired.value);
                    dot = math.clamp(dot, -1f, 1f);
                    float angleRad = 2f * math.acos(math.abs(dot));
                    float angleDeg = math.degrees(angleRad);

                    if (angleDeg < 0.5f) // Threshold in Grad; passe an (0.1–1.0)
                    {
                        tf.Rotation = desired; // Optional: nur setzen bei Bedarf
                        return;
                    }

                    if (angleDeg < 0.001f)
                    {
                        tf.Rotation = desired;
                    }
                    else
                    {
                        float tBlend = math.min(1f, (fm.RotationSpeed * DeltaTime) / angleDeg);
                        tf.Rotation = math.slerp(tf.Rotation, desired, tBlend);
                    }
                }
            }
            else
            {
                if (math.lengthsq(toTarget) < 1e-8f) return;
                quaternion desired = quaternion.LookRotationSafe(math.normalize(toTarget), new float3(0f, 1f, 0f));

                if (fm.RotationSpeed <= 0f)
                {
                    tf.Rotation = desired;
                }
                else
                {
                    float dot = math.dot(tf.Rotation.value, desired.value);
                    dot = math.clamp(dot, -1f, 1f);
                    float angleRad = 2f * math.acos(math.abs(dot));
                    float angleDeg = math.degrees(angleRad);

                    if (angleDeg < 0.5f) // Threshold in Grad; passe an (0.1–1.0)
                    {
                        tf.Rotation = desired; // Optional: nur setzen bei Bedarf
                        return;
                    }

                    if (angleDeg < 0.001f)
                    {
                        tf.Rotation = desired;
                    }
                    else
                    {
                        float tBlend = math.min(1f, (fm.RotationSpeed * DeltaTime) / angleDeg);
                        tf.Rotation = math.slerp(tf.Rotation, desired, tBlend);
                    }
                }
            }
        }
    }
}

/// <summary>
/// Füge diese Component an Entities, die zur Maus schauen sollen.
/// </summary>
public struct FaceMouseComponent : IComponentData
{
    public float RotationSpeed; // Grad pro Sekunde. 0 = instant
    public float PlaneY;        // Y-Höhe der Projektionsebene (z.B. Boden = 0)
    public byte LockPitch;      // 1 = nur yaw (XZ-Ebene), 0 = volle 3D-Ausrichtung
}
