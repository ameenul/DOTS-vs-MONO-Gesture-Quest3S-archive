using GestureBenchmark.Core;
using UnityEngine;

namespace GestureBenchmark.Mono
{
    /// <summary>
    /// Satu cube = satu objek dengan perilakunya sendiri (gaya OOP).
    /// Skala ditulis SETIAP frame (berubah atau tidak) supaya beban kerja tiap frame
    /// sama seperti versi DOTS, yang juga menulis skala semua entity setiap frame.
    /// </summary>
    public class MonoCube : MonoBehaviour
    {
        /// <summary>Jumlah cube yang skalanya berubah pada frame ini (di-reset manager tiap frame).</summary>
        public static int ChangedCount;

        Transform _tr;
        float _last = -1f;

        void Awake() => _tr = transform;

        void Update()
        {
            var m = MonoBenchmarkManager.Active;
            if (m == null) return;

            float s = CubeLayout.BaseSize * m.CurrentScale;
            if (s != _last) ChangedCount++;
            _last = s;
            _tr.localScale = new Vector3(s, s, s);
        }
    }
}
