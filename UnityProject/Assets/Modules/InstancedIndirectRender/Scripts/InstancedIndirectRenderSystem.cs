using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class InstancedIndirectRenderManagedSystem : SystemBase
{
    // Persistente managed Ressourcen (dürfen in SystemBase liegen)
    public ComputeBuffer InstanceBuffer { get; private set; }
    public ComputeBuffer ArgsBuffer { get; private set; }
    public NativeArray<float4x4> MatrixArray; // NativeArray ist in managed holder ok (Allocator.Persistent)
    public JobHandle DataPrepHandle = default;
    private MaterialPropertyBlock _mpb;
    private bool _mpbBoundToBuffer = false; // true, wenn _mpb bereits das aktuelle InstanceBuffer enthält

    public bool BuffersInitialized { get; private set; } = false;
    public int CurrentCapacity { get; private set; } = 0;
    public int PreviousFrameCount = 0;
    public uint IndexCountPerInstance = 0;
    readonly uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };
    // Bounds bleiben unverändert wie gewünscht
    readonly Bounds _worldBounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));

    protected override void OnCreate()
    {
        base.OnCreate();
    }

    /// <summary>
    /// Initialisiert Buffer / NativeArray (wenn Mesh/Material bekannt sind)
    /// </summary>
    public void EnsureInitialized(Mesh mesh, Material material, int initialCapacity = 1024)
    {
        if (BuffersInitialized) return;
        if (mesh == null || material == null) return;

        IndexCountPerInstance = (uint)mesh.GetIndexCount(0);
        _args[0] = IndexCountPerInstance;

        CurrentCapacity = math.max(32, initialCapacity);

        // ComputeBuffer / ArgsBuffer / NativeArray anlegen
        InstanceBuffer = new ComputeBuffer(CurrentCapacity, sizeof(float) * 16, ComputeBufferType.Structured);
        ArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        MatrixArray = new NativeArray<float4x4>(CurrentCapacity, Allocator.Persistent);

        // MaterialPropertyBlock einmalig anlegen und binden
        _mpb = new MaterialPropertyBlock();
        _mpb.SetBuffer("_PerInstanceMatrices", InstanceBuffer);
        _mpbBoundToBuffer = true;

        BuffersInitialized = true;
    }

    public void EnsureCapacity(int count)
    {
        if (count <= CurrentCapacity) return;

        // Warte auf evtl. laufenden Job, bevor du resize machst
        DataPrepHandle.Complete();

        // Dispose vorheriger Ressourcen
        InstanceBuffer?.Dispose();
        if (MatrixArray.IsCreated) MatrixArray.Dispose();

        int newCap = math.max(count, CurrentCapacity * 2);
        CurrentCapacity = newCap;

        InstanceBuffer = new ComputeBuffer(CurrentCapacity, sizeof(float) * 16, ComputeBufferType.Structured);
        MatrixArray = new NativeArray<float4x4>(CurrentCapacity, Allocator.Persistent);

        // MPB neu binden, da Buffer ersetzt wurde
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        _mpb.SetBuffer("_PerInstanceMatrices", InstanceBuffer);
        _mpbBoundToBuffer = true;
    }

    public void UploadAndDrawIfNeeded(Mesh mesh, Material material)
    {
        // Diese Methode wird vom ISystem (Main thread) aufgerufen
        if (!BuffersInitialized || mesh == null || material == null)
            return;

        // Warten auf Job (Frame N-1) — sicherstellen, dass MatrixArray fertig ist
        DataPrepHandle.Complete();

        if (PreviousFrameCount > 0)
        {
            // Upload der tatsächlich benötigten Matrizen
            InstanceBuffer.SetData(MatrixArray, 0, 0, PreviousFrameCount);
            _args[1] = (uint)PreviousFrameCount;
            ArgsBuffer.SetData(_args);

            // MPB ist nur gesetzt, wenn Buffer neu erstellt wurde. Keine redundanten SetBuffer-Aufrufe mehr pro Frame.
            if (!_mpbBoundToBuffer)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                _mpb.SetBuffer("_PerInstanceMatrices", InstanceBuffer);
                _mpbBoundToBuffer = true;
            }

            Graphics.DrawMeshInstancedIndirect(
                mesh, 0, material, _worldBounds, ArgsBuffer,
                0, _mpb, ShadowCastingMode.On, true, 0, null, LightProbeUsage.Off
            );
        }
    }

    protected override void OnUpdate()
    {
        // kein Runtime-Work hier – ISystem übernimmt Scheduling
    }

    protected override void OnDestroy()
    {
        // Clean up managed/unmanaged resources
        DataPrepHandle.Complete();

        InstanceBuffer?.Dispose();
        ArgsBuffer?.Dispose();
        if (MatrixArray.IsCreated) MatrixArray.Dispose();

        BuffersInitialized = false;
        base.OnDestroy();
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct InstancedIndirectRenderSystem : ISystem, ISystemStartStop
{
    private EntityQuery _renderQuery;

    // Wir speichern keine managed Felder hier (ISystem muss unmanaged bleiben).
    // Die managed Ressourcen (ComputeBuffer, Mesh, Material, NativeArray mit Allocator.Persistent)
    // leben im InstancedIndirectRenderManagedSystem (SystemBase), das wir vom ISystem aus aufrufen.

    // --- Job: Sammle Matrizen (burst-kompiliert) ---
    [BurstCompile]
    partial struct GatherMatricesJob : IJobEntity
    {
        // Wir schreiben direkt in das persistente MatrixArray. Die Job-Ausführung schreibt
        // nur bis entityInQueryIndex < actualEntityCount, also ist es sicher, das gesamte Array
        // zu übergeben (kein GetSubArray/Allokation pro Frame nötig).
        [WriteOnly] public NativeArray<float4x4> InstanceData;

        public void Execute([ReadOnly] in LocalToWorld ltw, [EntityIndexInQuery] int entityInQueryIndex)
        {
            InstanceData[entityInQueryIndex] = ltw.Value;
        }
    }

    // OnCreate: non-burst context, sicher für managed world calls
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InstancedIndirectRenderingComponent>();

        // Query einmalig erzeugen, NICHT im Update
        _renderQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<LocalToWorld>()
        );

        // managed owner system sicherstellen (wird bei Bedarf erzeugt)
        state.World.GetOrCreateSystemManaged<InstancedIndirectRenderManagedSystem>();
    }

    public void OnStartRunning(ref SystemState state) { }

    public void OnStopRunning(ref SystemState state) { }

    // WICHTIG: OnUpdate ist hier absichtlich **nicht** mit [BurstCompile] markiert,
    // damit wir managed APIs verwenden dürfen (z. B. das Managed-System oder Graphics.DrawMeshInstancedIndirect).
    // Die Jobs innerhalb bleiben geburstet.
    public void OnUpdate(ref SystemState state)
    {
        var managed = state.World.GetExistingSystemManaged<InstancedIndirectRenderManagedSystem>();
        if (managed == null) return;

        var cfg = SystemAPI.GetSingleton<InstancedIndirectRenderingComponent>();
        Mesh mesh = cfg.Mesh;
        Material mat = cfg.Material;

        if (mesh == null || mat == null) return;

        if (!managed.BuffersInitialized)
        {
            managed.EnsureInitialized(mesh, mat, 1024);
            if (!managed.BuffersInitialized) return;
        }

        // Upload+Draw von Frame N-1
        managed.UploadAndDrawIfNeeded(mesh, mat);

        // Hier nutzen wir die Query, die im OnCreate erzeugt wurde
        int currentFrameCount = _renderQuery.CalculateEntityCount();
        if (currentFrameCount == 0)
        {
            managed.PreviousFrameCount = 0;
            return;
        }

        managed.EnsureCapacity(currentFrameCount);

        // Kein GetSubArray mehr — Job schreibt direkt in das persistente MatrixArray.
        var job = new GatherMatricesJob
        {
            InstanceData = managed.MatrixArray
        };

        JobHandle handle = job.ScheduleParallel(_renderQuery, state.Dependency);
        managed.DataPrepHandle = handle;
        state.Dependency = handle;

        managed.PreviousFrameCount = currentFrameCount;
    }

    public void OnDestroy(ref SystemState state)
    {
        // Nothing to do here. Managed SystemBase kümmert sich um Dispose in seinem OnDestroy.
    }
}
