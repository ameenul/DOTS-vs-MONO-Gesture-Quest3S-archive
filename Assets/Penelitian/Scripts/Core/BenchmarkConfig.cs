using UnityEngine;

namespace GestureBenchmark.Core
{
    /// <summary>
    /// Konstanta yang DIPAKAI BERSAMA oleh versi Mono dan DOTS.
    /// Mengubah nilai di sini otomatis berlaku untuk kedua metode,
    /// sehingga algoritma dan beban kerja keduanya tetap identik.
    /// </summary>
    public static class DetectorConfig
    {
        public const float ZoomThreshold          = 0.15f;
        public const float SmallMovementThreshold = 0.005f;
        public const int   MinConsistentFrames    = 5;
    }

    /// <summary>
    /// Aturan perubahan skala cube dari hasil deteksi.
    /// Skala relatif terhadap awal trial: 1 + (perubahan jarak sejak awal trial),
    /// dibatasi 0,5 – 1,5 supaya cube tidak saling tumpang tindih di grid.
    /// Ditulis dengan operasi float biasa (tanpa Mathf) agar bisa dipanggil
    /// dari kode Mono maupun dari sistem DOTS yang dikompilasi Burst.
    /// </summary>
    public static class ScaleRule
    {
        public const float Gain = 1f;
        public const float Min  = 0.5f;
        public const float Max  = 1.5f;

        public static float Compute(float deltaFromStart)
        {
            float s = 1f + Gain * deltaFromStart;
            if (s < Min) s = Min;
            else if (s > Max) s = Max;
            return s;
        }
    }

    /// <summary>
    /// Tata letak cube: kubus 3D n×n×n di depan pengguna.
    /// Jarak antar cube 0,1 m dan ukuran dasar 0,05 m, jadi pada skala
    /// maksimum 1,5 (0,075 m) cube tetap tidak bersentuhan.
    /// </summary>
    public static class CubeLayout
    {
        public const float BaseSize = 0.05f;
        public const float Spacing  = 0.10f;
        public static readonly Vector3 Center = new Vector3(0f, 1.4f, 3.0f);

        public static int SideCount(int count)
        {
            int n = 1;
            while (n * n * n < count) n++;
            return n;
        }

        /// <summary>Jarak pusat kumpulan cube dari headset (m), bila posisi pandang tersedia.</summary>
        public const float ViewDistance = 3.0f;

        /// <summary>
        /// Posisi cube ke-index. Bila Runner sudah mencatat posisi pandang headset
        /// (BenchmarkContext.HasView), kumpulan cube diletakkan ViewDistance di depan headset
        /// setinggi mata, mengikuti arah hadap (yaw) headset. Posisi pandang dicatat SEKALI
        /// per sesi, jadi semua run Mono dan DOTS melihat susunan yang sama persis.
        /// Tanpa Runner (uji di editor), dipakai titik tetap Center.
        /// Rotasi tiap cube tetap identitas; hanya posisinya yang diputar.
        /// </summary>
        public static Vector3 Position(int index, int count)
        {
            int n = SideCount(count);
            int x = index % n;
            int y = (index / n) % n;
            int z = index / (n * n);
            float off = (n - 1) * Spacing * 0.5f;
            var local = new Vector3(x * Spacing - off, y * Spacing - off, z * Spacing - off);

            if (!BenchmarkContext.HasView) return Center + local;

            var rot = Quaternion.Euler(0f, BenchmarkContext.ViewYaw, 0f);
            return BenchmarkContext.ViewPos + rot * (new Vector3(0f, 0f, ViewDistance) + local);
        }
    }
}
