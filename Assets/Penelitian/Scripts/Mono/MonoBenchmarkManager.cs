using System.Collections.Generic;
using GestureBenchmark.Core;
using UnityEngine;

namespace GestureBenchmark.Mono
{
    /// <summary>
    /// Versi OBJECT-ORIENTED (MonoBehaviour), "Cara B":
    ///   - manager ini membaca satu baris dataset per frame dan menjalankan ZoomGestureDetector
    ///   - setiap cube adalah GameObject dengan MonoCube.Update() miliknya sendiri
    ///
    /// Urutan eksekusi -100 menjamin manager selalu selesai sebelum Update() cube (0),
    /// sehingga semua cube membaca hasil deteksi frame yang sama.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MonoBenchmarkManager : MonoBehaviour, IBenchmarkTarget
    {
        public GameObject cubePrefab;

        public static MonoBenchmarkManager Active { get; private set; }

        /// <summary>Faktor skala yang dibaca semua cube pada frame ini.</summary>
        public float CurrentScale { get; private set; } = 1f;

        public BenchmarkMethod Method => BenchmarkMethod.Mono;
        public bool IsDone => _phase == 2;

        GestureDataset _ds;
        ZoomGestureDetector _detector;
        readonly List<GameObject> _cubes = new List<GameObject>();
        int _cubeCount;
        int _phase;          // 0 warmup, 1 replay, 2 selesai
        int _nextRow;
        int _row = -1;
        int _detected;

        public void Setup(int cubeCount, GestureDataset dataset)
        {
            Active = this;
            _ds = dataset;
            _cubeCount = cubeCount;
            _detector = new ZoomGestureDetector(
                DetectorConfig.ZoomThreshold,
                DetectorConfig.SmallMovementThreshold,
                DetectorConfig.MinConsistentFrames);

            // Cube di root scene (tanpa parent) supaya tidak ada beban hierarki tambahan.
            for (int i = 0; i < cubeCount; i++)
            {
                var go = Instantiate(cubePrefab, CubeLayout.Position(i, cubeCount), Quaternion.identity);
                go.name = "Cube_" + i;
                _cubes.Add(go);
            }
            _phase = 0;
        }

        public void BeginReplay() { _phase = 1; _nextRow = 0; }

        void Update()
        {
            MonoCube.ChangedCount = 0;   // dihitung ulang oleh cube di frame ini

            if (_phase == 2) { _row = -1; return; }

            int r;
            if (_phase == 0) r = 0;                       // warmup: baris 0 terus-menerus
            else
            {
                if (_nextRow >= _ds.Count) { _phase = 2; _row = -1; return; }
                r = _nextRow++;
            }

            if (_phase == 1 && _ds.IsTrialStart[r])
            {
                _detector.Reset();
                CurrentScale = 1f;
            }

            ref readonly var f = ref _ds.Frames[r];
            var res = _detector.ProcessFrame(f.LeftPosition, f.RightPosition);
            _detected = (int)res.type;
            if (res.type != GestureType.None)
                CurrentScale = ScaleRule.Compute(res.deltaFromStart);

            _row = r;
        }

        public bool TryGetFrame(out FrameProbe probe)
        {
            probe = new FrameProbe
            {
                row       = _row,
                detected  = _detected,
                allScaled = MonoCube.ChangedCount == _cubeCount,
                scale     = CurrentScale
            };
            return true;
        }

        public void Teardown()
        {
            foreach (var c in _cubes) if (c != null) Destroy(c);
            _cubes.Clear();
            if (Active == this) Active = null;
        }
    }
}
