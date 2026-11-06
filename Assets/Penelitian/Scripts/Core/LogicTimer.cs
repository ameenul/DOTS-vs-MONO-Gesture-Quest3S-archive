using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace GestureBenchmark.Core
{
    /// <summary>
    /// Stopwatch yang mengapit SELURUH fase Update di player loop Unity.
    ///
    ///   [Begin] ── Update() semua MonoBehaviour ── SimulationSystemGroup (DOTS) ── [End]
    ///
    /// Di dalam jendela ini berjalan seluruh logika benchmark:
    ///   - Mono : manager (baca dataset + deteksi) lalu Update() milik tiap cube
    ///   - DOTS : sistem deteksi, job skala (Burst, paralel), dan TransformSystemGroup;
    ///            sistem probe di akhir grup menunggu semua job selesai, jadi kerja
    ///            worker thread ikut terhitung.
    /// Rendering dan waktu menunggu layar (VSync/XR) berada di luar jendela.
    ///
    /// Hasil = "waktu pemrosesan logika" (logic_ms). Dipasang otomatis.
    /// </summary>
    public static class LogicTimer
    {
        static readonly Stopwatch _sw = new Stopwatch();

        /// <summary>Waktu (ms sejak aplikasi mulai) ketika frame ini masuk fase Update.</summary>
        public static double FrameStartMs { get; private set; }

        /// <summary>Dipanggil di awal fase Update (sebelum stopwatch mulai).</summary>
        public static event Action FrameBegin;

        /// <summary>Dipanggil di akhir fase Update (setelah stopwatch berhenti), membawa logic_ms frame ini.</summary>
        public static event Action<double> FrameEnd;

        // ---- jendela kedua: persiapan rendering di main thread ----
        // [RenderBegin] awal PreLateUpdate ── LateUpdate, PresentationSystemGroup (DOTS: upload data
        // Entities Graphics) ── PostLateUpdate (sinkron Transform->renderer, culling, penyusunan
        // perintah gambar SRP) ── [RenderEnd] akhir PostLateUpdate.
        // Pekerjaan render thread dan GPU TIDAK termasuk (angka ini batas bawah biaya rendering).
        static readonly Stopwatch _swRender = new Stopwatch();

        /// <summary>Waktu persiapan rendering main thread frame TERAKHIR yang selesai (ms).</summary>
        public static double LastRenderMs { get; private set; } = double.NaN;

        struct LogicTimerBegin { }
        struct LogicTimerEnd { }
        struct RenderTimerBegin { }
        struct RenderTimerEnd { }

        // AfterSceneLoad: world DOTS default sudah dibuat dan SimulationSystemGroup
        // sudah ditambahkan ke akhir fase Update, jadi End kita berada SETELAHNYA.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Contains(loop, typeof(LogicTimerBegin))) return;

            Insert<Update>(ref loop, new PlayerLoopSystem
            {
                type = typeof(LogicTimerBegin),
                updateDelegate = OnBegin
            }, atStart: true);

            Insert<Update>(ref loop, new PlayerLoopSystem
            {
                type = typeof(LogicTimerEnd),
                updateDelegate = OnEnd
            }, atStart: false);

            Insert<PreLateUpdate>(ref loop, new PlayerLoopSystem
            {
                type = typeof(RenderTimerBegin),
                updateDelegate = () => _swRender.Restart()
            }, atStart: true);

            Insert<PostLateUpdate>(ref loop, new PlayerLoopSystem
            {
                type = typeof(RenderTimerEnd),
                updateDelegate = () => { _swRender.Stop(); LastRenderMs = _swRender.Elapsed.TotalMilliseconds; }
            }, atStart: false);

            PlayerLoop.SetPlayerLoop(loop);
            DumpOrder(loop);
        }

        static void OnBegin()
        {
            FrameStartMs = Time.realtimeSinceStartupAsDouble * 1000.0;
            FrameBegin?.Invoke();
            _sw.Restart();
        }

        static void OnEnd()
        {
            _sw.Stop();
            FrameEnd?.Invoke(_sw.Elapsed.TotalMilliseconds);
        }

        static bool Insert<T>(ref PlayerLoopSystem sys, PlayerLoopSystem add, bool atStart)
        {
            if (sys.type == typeof(T))
            {
                var list = new List<PlayerLoopSystem>(sys.subSystemList ?? new PlayerLoopSystem[0]);
                if (atStart) list.Insert(0, add); else list.Add(add);
                sys.subSystemList = list.ToArray();
                return true;
            }
            if (sys.subSystemList == null) return false;
            for (int i = 0; i < sys.subSystemList.Length; i++)
            {
                var s = sys.subSystemList[i];
                if (Insert<T>(ref s, add, atStart)) { sys.subSystemList[i] = s; return true; }
            }
            return false;
        }

        static bool Contains(PlayerLoopSystem sys, Type t)
        {
            if (sys.type == t) return true;
            if (sys.subSystemList == null) return false;
            foreach (var s in sys.subSystemList) if (Contains(s, t)) return true;
            return false;
        }

        /// <summary>
        /// Cetak urutan fase Update ke log untuk verifikasi:
        /// LogicTimerBegin harus paling atas, LogicTimerEnd paling bawah
        /// (setelah ScriptRunBehaviourUpdate dan SimulationSystemGroup).
        /// </summary>
        static void DumpOrder(PlayerLoopSystem root)
        {
            var sb = new StringBuilder("[LogicTimer] urutan fase Update / PreLateUpdate / PostLateUpdate:\n");
            foreach (var phase in root.subSystemList)
            {
                if (phase.type != typeof(Update) && phase.type != typeof(PreLateUpdate) &&
                    phase.type != typeof(PostLateUpdate)) continue;
                sb.Append(phase.type.Name).Append('\n');
                foreach (var s in phase.subSystemList) sb.Append("   - ").Append(s.type?.Name).Append('\n');
            }
            UnityEngine.Debug.Log(sb.ToString());
        }
    }
}
