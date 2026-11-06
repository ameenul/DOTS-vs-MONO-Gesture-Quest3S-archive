using System.IO;
using UnityEngine;

namespace GestureBenchmark.Core
{
    public enum BenchmarkMethod { Mono = 0, Dots = 1 }

    /// <summary>Parameter run yang sedang berjalan, diisi oleh BenchmarkRunner.</summary>
    public static class BenchmarkContext
    {
        /// <summary>false = scene dibuka langsung (uji manual), pakai nilai default di Inspector.</summary>
        public static bool   FromRunner;
        public static bool   Pilot;        // true = uji pendahuluan (kalibrasi kategori beban)
        public static string Category;     // "rendah" / "sedang" / "berat" / "pilot" / "uji"
        public static int    CubeCount;
        public static int    RunId;
        public static float  WarmupSec;
        /// <summary>Batas baris dataset yang direkam (0 = seluruh dataset). Dipakai mode pilot.</summary>
        public static int    MaxRows;

        /// <summary>Posisi & arah hadap headset, dicatat Runner sekali per sesi (untuk menaruh cube di depan pandangan).</summary>
        public static bool    HasView;
        public static Vector3 ViewPos;
        public static float   ViewYaw;

        public static bool   Finished;
        public static string LastOutputPath;

        public static string SceneName(BenchmarkMethod m) =>
            m == BenchmarkMethod.Mono ? "Penelitian_Mono" : "Penelitian_DOTS";

        public static string OutputDir => Path.Combine(Application.persistentDataPath, "Penelitian");
        public static string PilotDir  => Path.Combine(OutputDir, "Pilot");

        static string M(BenchmarkMethod m) => m == BenchmarkMethod.Mono ? "mono" : "dots";

        /// <summary>Utama: Penelitian/mono_sedang_01000_run03.csv</summary>
        public static string OutputPath(BenchmarkMethod m, string category, int cubes, int run) =>
            Path.Combine(OutputDir, $"{M(m)}_{category}_{cubes:D5}_run{run:D2}.csv");

        /// <summary>Pilot: Penelitian/Pilot/pilot_mono_01000.csv</summary>
        public static string PilotPath(BenchmarkMethod m, int cubes) =>
            Path.Combine(PilotDir, $"pilot_{M(m)}_{cubes:D5}.csv");
    }

    /// <summary>Data satu frame yang dilaporkan oleh metode (Mono/DOTS) ke logger.</summary>
    public struct FrameProbe
    {
        public int   row;          // baris dataset yang diproses frame ini (-1 = tidak ada)
        public int   detected;     // GestureType sebagai int
        public bool  allScaled;    // true bila SEMUA cube berubah skala di frame ini
        public float scale;        // faktor skala (ScaleRule) frame ini
    }

    /// <summary>Kontrak yang diimplementasikan versi Mono dan versi DOTS.</summary>
    public interface IBenchmarkTarget
    {
        BenchmarkMethod Method { get; }
        void Setup(int cubeCount, GestureDataset dataset);
        void BeginReplay();
        bool IsDone { get; }
        bool TryGetFrame(out FrameProbe probe);
        void Teardown();
    }
}
