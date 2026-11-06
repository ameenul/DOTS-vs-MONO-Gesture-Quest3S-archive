using UnityEngine;

namespace GestureBenchmark.Core
{
    /// <summary>
    /// Satu baris dataset = satu frame.
    ///
    /// Dibuat STRUCT (bukan class seperti di dokumen) supaya penambahan ke List
    /// tidak mengalokasikan objek baru di heap tiap frame. Recording jalan ~72 FPS
    /// di Quest, jadi alokasi per frame akan menimbulkan tekanan GC yang bisa
    /// menyebabkan frame hitch dan mengganggu kualitas data.
    ///
    /// Urutan field mengikuti urutan kolom CSV di dokumen rancangan.
    /// </summary>
    [System.Serializable]
    public struct GestureFrameData
    {
        public int    trialId;
        public int    frameIndex;
        public float  timeMs;

        public float  leftX, leftY, leftZ;
        public float  rightX, rightY, rightZ;

        /// <summary>
        /// Jarak controller kiri-kanan. Disimpan HANYA untuk validasi dan grafik.
        /// Saat benchmark, OOP dan DOTS wajib menghitung ulang sendiri dari posisi,
        /// supaya beban komputasi kedua metode setara.
        /// </summary>
        public float  distance;

        /// <summary>Label ground truth level trial, diulang tiap baris agar CSV mudah difilter.</summary>
        public string gestureLabel;

        public static GestureFrameData Create(
            int trialId, int frameIndex, float timeMs,
            Vector3 left, Vector3 right, float distance, string label)
        {
            return new GestureFrameData
            {
                trialId      = trialId,
                frameIndex   = frameIndex,
                timeMs       = timeMs,
                leftX        = left.x,
                leftY        = left.y,
                leftZ        = left.z,
                rightX       = right.x,
                rightY       = right.y,
                rightZ       = right.z,
                distance     = distance,
                gestureLabel = label
            };
        }

        public Vector3 LeftPosition  => new Vector3(leftX,  leftY,  leftZ);
        public Vector3 RightPosition => new Vector3(rightX, rightY, rightZ);
    }
}