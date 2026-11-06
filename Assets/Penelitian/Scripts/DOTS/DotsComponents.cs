using Unity.Entities;
using Unity.Mathematics;

namespace GestureBenchmark.Dots
{
    /// <summary>Penanda entity cube. Cube DOTS hanya berisi data, tanpa method/Update.</summary>
    public struct CubeTag : IComponentData { }

    /// <summary>Dataset dalam bentuk blob (memori tak terkelola, bisa dibaca Burst).</summary>
    public struct DatasetBlob
    {
        public BlobArray<float3> Left;
        public BlobArray<float3> Right;
        public BlobArray<byte>   TrialStart;
    }

    /// <summary>
    /// Singleton status replay + status detector versi DOTS.
    /// Field detector sama persis dengan field privat ZoomGestureDetector (versi OOP).
    /// </summary>
    public struct DotsReplay : IComponentData
    {
        public BlobAssetReference<DatasetBlob> Data;

        // replay
        public int   Phase;      // 0 warmup, 1 replay, 2 selesai
        public int   NextRow;
        public int   Row;        // baris yang diproses frame ini (-1 = tidak ada)
        public int   Detected;   // 0 none, 1 zoom_in, 2 zoom_out
        public float Scale;      // faktor skala (ScaleRule)
        public float BaseSize;

        // status detector (cermin ZoomGestureDetector)
        public bool  HasStarted;
        public float StartDistance;
        public float PreviousDistance;
        public int   ConsistentZoomInFrames;
        public int   ConsistentZoomOutFrames;
        public float ZoomThreshold;
        public float SmallMovementThreshold;
        public int   MinConsistentFrames;

        // probe perubahan skala
        public Entity ProbeEntity;
        public float  PrevProbeScale;
        public bool   AllScaled;
    }
}
