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

        var job = new FaceMouseJob
        {
            RayOrigin = rayOrigin,
            RayDir = rayDir,
            DeltaTime = dt
        };
        this.Dependency = job.ScheduleParallel(this.Dependency);
    }

    [BurstCompile]
    partial struct FaceMouseJob : IJobEntity
    {
        public float3 RayOrigin;
        public float3 RayDir;
        public float DeltaTime;

        public void Execute(ref LocalToWorld ltw, in FaceMouseComponent fm)
        {
            const float eps = 1e-6f;

            // Extrahiere Position/Rotation aus ltw
            float3 pos = ltw.Position;
            quaternion rot = ltw.Rotation;

            // Berechne Zielpunkt auf Ebene
            float3 targetWorld;
            if (math.abs(RayDir.y) > eps)
            {
                float t = (fm.PlaneY - RayOrigin.y) / RayDir.y;
                targetWorld = RayOrigin + RayDir * t;
            }
            else
            {
                targetWorld = new float3(RayOrigin.x, fm.PlaneY, RayOrigin.z);
            }

            // Berechne Rotation
            float3 toTarget = targetWorld - pos;
            if (fm.LockPitch == 1) toTarget.y = 0f;

            if (math.lengthsq(toTarget) > eps)
            {
                quaternion desired = quaternion.LookRotationSafe(math.normalize(toTarget), new float3(0f, 1f, 0f));

                // Slerp oder sofort
                rot = fm.RotationSpeed <= 0f ? desired :
                      math.slerp(rot, desired, math.min(1f, (fm.RotationSpeed * DeltaTime) / math.degrees(2f * math.acos(math.clamp(math.dot(rot.value, desired.value), -1f, 1f)))));
            }

            // Update LocalToWorld direkt
            ltw.Value = float4x4.TRS(pos, rot, new float3(1f));
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
