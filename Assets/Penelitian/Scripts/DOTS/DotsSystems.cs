using GestureBenchmark.Core;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace GestureBenchmark.Dots
{
    /// <summary>
    /// Sistem 1 — baca satu baris dataset per frame dan deteksi gesture.
    /// Logika di bawah adalah CERMIN BARIS PER BARIS dari ZoomGestureDetector.ProcessFrame
    /// dan MonoBenchmarkManager.Update. Bila salah satunya diubah, yang lain wajib ikut diubah.
    /// FloatMode.Strict: Burst tidak boleh mengubah urutan/menggabungkan operasi float,
    /// supaya hasil jarak sama dengan versi Mono.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct DotsGestureSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<DotsReplay>();

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        public void OnUpdate(ref SystemState state)
        {
            ref var r = ref SystemAPI.GetSingletonRW<DotsReplay>().ValueRW;
            ref var data = ref r.Data.Value;

            if (r.Phase == 2) { r.Row = -1; return; }

            int row;
            if (r.Phase == 0) row = 0;                      // warmup: baris 0 terus-menerus
            else
            {
                if (r.NextRow >= data.Left.Length) { r.Phase = 2; r.Row = -1; return; }
                row = r.NextRow++;
            }

            if (r.Phase == 1 && data.TrialStart[row] == 1)
            {
                // == ZoomGestureDetector.Reset()
                r.HasStarted              = false;
                r.StartDistance           = 0f;
                r.PreviousDistance        = 0f;
                r.ConsistentZoomInFrames  = 0;
                r.ConsistentZoomOutFrames = 0;
                r.Scale = 1f;
            }

            // == ZoomGestureDetector.ProcessFrame()
            float currentDistance = math.distance(data.Left[row], data.Right[row]);
            int detected = 0;
            float deltaFromStart = 0f;

            if (!r.HasStarted)
            {
                r.HasStarted       = true;
                r.StartDistance    = currentDistance;
                r.PreviousDistance = currentDistance;
            }
            else
            {
                float deltaDistance = currentDistance - r.PreviousDistance;
                deltaFromStart      = currentDistance - r.StartDistance;

                if (deltaDistance > r.SmallMovementThreshold)
                {
                    r.ConsistentZoomInFrames++;
                    r.ConsistentZoomOutFrames = 0;
                }
                else if (deltaDistance < -r.SmallMovementThreshold)
                {
                    r.ConsistentZoomOutFrames++;
                    r.ConsistentZoomInFrames = 0;
                }

                if (deltaFromStart >= r.ZoomThreshold &&
                    r.ConsistentZoomInFrames >= r.MinConsistentFrames)
                    detected = 1;
                else if (deltaFromStart <= -r.ZoomThreshold &&
                         r.ConsistentZoomOutFrames >= r.MinConsistentFrames)
                    detected = 2;

                r.PreviousDistance = currentDistance;
            }

            r.Detected = detected;
            if (detected != 0) r.Scale = ScaleRule.Compute(deltaFromStart);
            r.Row = row;
        }
    }

    /// <summary>
    /// Sistem 2 — ubah skala SEMUA cube. Satu job Burst, dijalankan paralel di worker thread.
    /// Ditulis setiap frame (seperti MonoCube) agar beban kerja setara.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DotsGestureSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct DotsScaleSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<DotsReplay>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var r = SystemAPI.GetSingleton<DotsReplay>();
            new ScaleJob { Size = r.BaseSize * r.Scale }.ScheduleParallel();
        }
    }

    [BurstCompile]
    [WithAll(typeof(CubeTag))]
    public partial struct ScaleJob : IJobEntity
    {
        public float Size;
        void Execute(ref LocalTransform t) => t.Scale = Size;
    }

    /// <summary>
    /// Sistem 3 — probe di AKHIR SimulationSystemGroup (setelah TransformSystemGroup).
    /// Membaca LocalToWorld (nilai yang dipakai untuk menggambar) dari satu cube.
    /// GetComponentData menunggu semua job yang menulis LocalToWorld selesai, sehingga
    /// kerja worker thread DOTS ikut masuk ke jendela LogicTimer.
    /// Satu job memproses semua cube dengan nilai yang sama di frame yang sama,
    /// jadi satu cube mewakili semuanya (berubah semua atau tidak sama sekali).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct DotsScaleProbeSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<DotsReplay>();

        public void OnUpdate(ref SystemState state)
        {
            var e = SystemAPI.GetSingletonEntity<DotsReplay>();
            var r = state.EntityManager.GetComponentData<DotsReplay>(e);
            var ltw = state.EntityManager.GetComponentData<LocalToWorld>(r.ProbeEntity);
            float s = ltw.Value.c0.x;              // rotasi identitas -> c0.x = skala
            r.AllScaled = s != r.PrevProbeScale;
            r.PrevProbeScale = s;
            state.EntityManager.SetComponentData(e, r);
        }
    }
}
