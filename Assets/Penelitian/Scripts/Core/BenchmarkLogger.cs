using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GestureBenchmark.Core
{
    /// <summary>
    /// Menyimpan data per frame ke array yang sudah dialokasikan di awal
    /// (tanpa alokasi memori selama rekaman), lalu menulis CSV sekali di akhir run.
    ///
    /// Kolom CSV:
    ///   seq              urutan baris log
    ///   unity_frame      Time.frameCount
    ///   t_start_ms       waktu awal fase Update frame ini  -> selisih antar baris = frame time (dasar FPS)
    ///   real_dt_prev_ms  Time.unscaledDeltaTime = lama frame SEBELUMNYA (pembanding t_start_ms)
    ///   logic_ms         waktu pemrosesan logika (Stopwatch LogicTimer, fase Update)
    ///   render_ms        waktu persiapan rendering main thread frame yang SAMA
    ///                    (Stopwatch PreLateUpdate..PostLateUpdate; diisi saat frame berikutnya dicatat)
    ///   trial_id, row, label   posisi di dataset dan ground truth
    ///   detected         hasil deteksi frame ini (0 none, 1 zoom_in, 2 zoom_out)
    ///   all_scaled       1 bila semua cube berubah skala di frame ini
    ///   scale            faktor skala frame ini
    ///   cpu_main_ms, present_wait_ms, gpu_ms   FrameTimingManager (pelengkap; di Quest/OpenXR
    ///                    cpu_main termasuk waktu menunggu XR dan gpu_ms = 0, jadi tidak dipakai)
    /// </summary>
    public sealed class BenchmarkLogger
    {
        struct Rec
        {
            public int unityFrame;
            public double tStartMs;
            public float realDtPrevMs, logicMs, renderMs;
            public int row, detected;
            public byte allScaled;
            public float scale;
            public float cpuMainMs, presentWaitMs, gpuMs;
        }

        readonly Rec[] _recs;
        int _n;
        readonly FrameTiming[] _ft = new FrameTiming[1];

        public int Count => _n;

        public BenchmarkLogger(int capacity) { _recs = new Rec[capacity]; }

        public void Add(in FrameProbe p, double logicMs)
        {
            if (_n >= _recs.Length) return;

            // render_ms frame sebelumnya baru diketahui sekarang (PostLateUpdate-nya sudah lewat)
            if (_n > 0 && _recs[_n - 1].unityFrame == Time.frameCount - 1)
                _recs[_n - 1].renderMs = (float)LogicTimer.LastRenderMs;

            ref var r = ref _recs[_n++];
            r.renderMs     = float.NaN;
            r.unityFrame   = Time.frameCount;
            r.tStartMs     = LogicTimer.FrameStartMs;
            r.realDtPrevMs = Time.unscaledDeltaTime * 1000f;
            r.logicMs      = (float)logicMs;
            r.row          = p.row;
            r.detected     = p.detected;
            r.allScaled    = (byte)(p.allScaled ? 1 : 0);
            r.scale        = p.scale;

            if (FrameTimingManager.GetLatestTimings(1, _ft) > 0)
            {
                r.cpuMainMs     = (float)_ft[0].cpuMainThreadFrameTime;
                r.presentWaitMs = (float)_ft[0].cpuMainThreadPresentWaitTime;
                r.gpuMs         = (float)_ft[0].gpuFrameTime;
            }
            else
            {
                r.cpuMainMs = r.presentWaitMs = r.gpuMs = float.NaN;
            }
        }

        public void Save(string path, GestureDataset ds, BenchmarkMethod method, string category, int cubes, int run, float warmupSec)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(_n * 120 + 1024);

            sb.Append("# method=").Append(method == BenchmarkMethod.Mono ? "mono" : "dots").Append('\n');
            sb.Append("# kategori=").Append(category).Append('\n');
            sb.Append("# cubes=").Append(cubes).Append('\n');
            sb.Append("# run=").Append(run).Append('\n');
            sb.Append("# warmup_sec=").Append(warmupSec.ToString(ci)).Append('\n');
            sb.Append("# display_hz=").Append(QuestPerfLock.CurrentHz().ToString("F1", ci)).Append('\n');
            sb.Append("# dataset=").Append(ds.SourceName).Append('\n');
            sb.Append("# view=").Append(BenchmarkContext.HasView
                ? $"{BenchmarkContext.ViewPos.x.ToString("F2", ci)};{BenchmarkContext.ViewPos.y.ToString("F2", ci)};{BenchmarkContext.ViewPos.z.ToString("F2", ci)};yaw{BenchmarkContext.ViewYaw.ToString("F1", ci)}"
                : "default").Append('\n');
            sb.Append("# device=").Append(SystemInfo.deviceModel).Append('\n');
            sb.Append("# unity=").Append(Application.unityVersion).Append('\n');
            sb.Append("# time=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci)).Append('\n');
            sb.Append("seq,unity_frame,t_start_ms,real_dt_prev_ms,logic_ms,render_ms,trial_id,row,label,detected,all_scaled,scale,cpu_main_ms,present_wait_ms,gpu_ms\n");

            for (int i = 0; i < _n; i++)
            {
                ref var r = ref _recs[i];
                var f = ds.Frames[r.row];
                sb.Append(i).Append(',')
                  .Append(r.unityFrame).Append(',')
                  .Append(r.tStartMs.ToString("F3", ci)).Append(',')
                  .Append(r.realDtPrevMs.ToString("F3", ci)).Append(',')
                  .Append(r.logicMs.ToString("F4", ci)).Append(',')
                  .Append(r.renderMs.ToString("F4", ci)).Append(',')
                  .Append(f.trialId).Append(',')
                  .Append(r.row).Append(',')
                  .Append(f.gestureLabel).Append(',')
                  .Append(r.detected).Append(',')
                  .Append(r.allScaled).Append(',')
                  .Append(r.scale.ToString("F5", ci)).Append(',')
                  .Append(r.cpuMainMs.ToString("F3", ci)).Append(',')
                  .Append(r.presentWaitMs.ToString("F3", ci)).Append(',')
                  .Append(r.gpuMs.ToString("F3", ci)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[BenchmarkLogger] {_n} frame tersimpan -> {path}");
        }
    }
}
