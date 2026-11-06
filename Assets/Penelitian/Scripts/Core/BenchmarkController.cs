using UnityEngine;

namespace GestureBenchmark.Core
{
    /// <summary>
    /// Pengatur satu run benchmark, dipakai di scene Mono dan scene DOTS:
    ///   1. Setup  : spawn N cube lewat target (Mono / DOTS)
    ///   2. Warmup : sistem berjalan dengan baris 0 dataset, data tidak dicatat
    ///   3. Replay : dataset diputar SATU BARIS PER FRAME, setiap frame dicatat
    ///               (mode pilot: berhenti setelah MaxRows baris)
    ///   4. Selesai: CSV disimpan, runner diberi tahu
    /// Urutan eksekusi -200: selalu jalan sebelum manager Mono (-100) dan cube (0).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class BenchmarkController : MonoBehaviour
    {
        [Tooltip("Komponen MonoBenchmarkManager atau DotsBenchmarkBridge di scene ini")]
        public MonoBehaviour target;
        public TextAsset dataset;

        [Header("Dipakai bila scene dibuka langsung (tanpa Runner)")]
        public int   defaultCubeCount = 1000;
        public float defaultWarmupSec = 5f;
        public int   defaultRunId     = 0;

        [Tooltip("Jumlah frame minimum fase warmup")]
        public int   minWarmupFrames  = 300;

        enum Phase { Warmup, Replay, Done }
        Phase _phase;
        IBenchmarkTarget _t;
        GestureDataset _ds;
        BenchmarkLogger _log;
        int _cubes, _run, _maxRows;
        bool _pilot;
        string _category;
        float _warmup;
        double _warmupStart;
        int _warmupFrames;

        void Start()
        {
            _t = target as IBenchmarkTarget;
            if (_t == null) { Debug.LogError("[Controller] target bukan IBenchmarkTarget"); enabled = false; return; }

            bool fromRunner = BenchmarkContext.FromRunner;
            _cubes    = fromRunner ? BenchmarkContext.CubeCount : defaultCubeCount;
            _run      = fromRunner ? BenchmarkContext.RunId     : defaultRunId;
            _warmup   = fromRunner ? BenchmarkContext.WarmupSec : defaultWarmupSec;
            _pilot    = fromRunner && BenchmarkContext.Pilot;
            _category = fromRunner ? BenchmarkContext.Category  : "uji";
            _maxRows  = fromRunner ? BenchmarkContext.MaxRows   : 0;
            BenchmarkContext.Finished = false;

            _ds  = GestureDataset.Get(dataset);
            _log = new BenchmarkLogger(_ds.Count + 16);
            _t.Setup(_cubes, _ds);

            LogicTimer.FrameBegin += OnFrameBegin;
            LogicTimer.FrameEnd   += OnFrameEnd;

            _phase = Phase.Warmup;
            _warmupStart = Time.realtimeSinceStartupAsDouble;
            Debug.Log($"[Controller] {_t.Method} | {_category} | {_cubes} cube | run {_run} | warmup {_warmup}s | " +
                      (_maxRows > 0 ? $"{_maxRows} baris (pilot)" : $"{_ds.Count} baris"));
        }

        void Update()
        {
            if (_phase == Phase.Warmup) _warmupFrames++;

            // Warmup selesai bila waktu DAN jumlah frame minimum terpenuhi
            // (frame awal setelah scene dimuat bisa sangat lama, jadi waktu saja tidak cukup).
            if (_phase == Phase.Warmup &&
                Time.realtimeSinceStartupAsDouble - _warmupStart >= _warmup &&
                _warmupFrames >= minWarmupFrames)
            {
                _t.BeginReplay();
                _phase = Phase.Replay;
            }
            else if (_phase == Phase.Replay && (_t.IsDone || (_maxRows > 0 && _log.Count >= _maxRows)))
            {
                string path = _pilot
                    ? BenchmarkContext.PilotPath(_t.Method, _cubes)
                    : BenchmarkContext.OutputPath(_t.Method, _category, _cubes, _run);
                _log.Save(path, _ds, _t.Method, _category, _cubes, _run, _warmup);
                BenchmarkContext.LastOutputPath = path;
                BenchmarkContext.Finished = true;
                _phase = Phase.Done;
            }
        }

        static void OnFrameBegin() => FrameTimingManager.CaptureFrameTimings();

        void OnFrameEnd(double logicMs)
        {
            if (_phase != Phase.Replay) return;
            if (_maxRows > 0 && _log.Count >= _maxRows) return;
            if (_t.TryGetFrame(out var p) && p.row >= 0) _log.Add(p, logicMs);
        }

        void OnDestroy()
        {
            LogicTimer.FrameBegin -= OnFrameBegin;
            LogicTimer.FrameEnd   -= OnFrameEnd;
            _t?.Teardown();
        }
    }
}
