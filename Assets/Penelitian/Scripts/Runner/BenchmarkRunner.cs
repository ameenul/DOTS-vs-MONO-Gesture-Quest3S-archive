using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GestureBenchmark.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GestureBenchmark.Runner
{
    public enum RunnerMode
    {
        /// <summary>Uji pendahuluan: sapuan jumlah cube, 1 run singkat per metode, untuk menentukan batas kategori.</summary>
        Pilot,
        /// <summary>Pengujian utama: 3 kategori beban × N run × 2 metode, dataset diputar penuh.</summary>
        Utama
    }

    [Serializable]
    public struct LoadCategory
    {
        public string name;   // rendah / sedang / berat
        public int    cubes;
    }

    /// <summary>
    /// Menjalankan pengujian otomatis di Quest.
    ///
    /// MODE PILOT (kalibrasi, ±15 menit):
    ///   untuk setiap jumlah cube di pilotCubeCounts -> Mono lalu DOTS, masing-masing 1 run singkat
    ///   hasil: Penelitian/Pilot/pilot_mono_01000.csv dst. -> olah dengan analisis.py --pilot
    ///
    /// MODE UTAMA:
    ///   untuk setiap kategori (rendah, sedang, berat) -> setiap run -> Mono dan DOTS (urutan diselang-seling)
    ///   run: muat scene metode -> warmup -> replay seluruh dataset -> simpan CSV -> kembali -> pendinginan
    ///   hasil: Penelitian/mono_sedang_01000_run03.csv dst.
    ///
    /// Run yang file CSV-nya sudah ada otomatis DILEWATI, jadi pengujian bisa dilanjutkan di sesi lain.
    /// </summary>
    public class BenchmarkRunner : MonoBehaviour
    {
        [Tooltip("Pilot sudah dijalankan (Quest 3S, 2026-11-01): Mono turun di bawah 72 FPS mulai 8.000 cube")]
        public RunnerMode mode = RunnerMode.Utama;

        [Header("Mode Pilot")]
        public int[] pilotCubeCounts = { 100, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        [Tooltip("Jumlah baris dataset yang direkam per run pilot (2160 ≈ 30 detik di 72 FPS)")]
        public int   pilotRows        = 2160;
        public float pilotWarmupSec   = 5f;
        public float pilotCooldownSec = 10f;

        [Header("Mode Utama")]
        public LoadCategory[] categories =
        {
            // hasil pilot Quest 3S: 1.000 = keduanya stabil 72 FPS; 4.000 = Mono median 72 FPS tapi p99 > 13,9 ms;
            // 8.000 = Mono turun ke ±61 FPS, DOTS tetap 72 FPS
            new LoadCategory { name = "rendah", cubes = 1000 },
            new LoadCategory { name = "sedang", cubes = 4000 },
            new LoadCategory { name = "berat",  cubes = 8000 },
        };
        public int   runsPerCategory = 10;
        public float warmupSec       = 10f;
        public float cooldownSec     = 60f;

        [Header("Umum")]
        public bool  includeMono     = true;
        public bool  includeDots     = true;
        public float startDelaySec   = 5f;
        public float targetDisplayHz = 72f;

        [Header("UI status (opsional)")]
        public TextMesh statusText;

        static BenchmarkRunner _instance;
        const string RunnerScene = "Penelitian_Runner";

        struct Job { public BenchmarkMethod method; public string category; public int cubes; public int run; }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        IEnumerator Start()
        {
            if (_instance != this) yield break;

            bool pilot = mode == RunnerMode.Pilot;
            try { QuestPerfLock.Apply(targetDisplayHz); }
            catch (Exception e) { Debug.LogError("[Runner] PerfLock gagal, pengujian tetap jalan: " + e); }
            Status($"Penelitian DOTS vs Mono\nMODE {(pilot ? "PILOT" : "UTAMA")}\nMulai dalam {startDelaySec:F0} detik...");
            yield return new WaitForSecondsRealtime(startDelaySec);

            // Catat posisi & arah hadap headset SEKALI untuk seluruh sesi.
            var cam = Camera.main;
            if (cam != null)
            {
                BenchmarkContext.ViewPos = cam.transform.position;
                BenchmarkContext.ViewYaw = cam.transform.eulerAngles.y;
                BenchmarkContext.HasView = true;
                Debug.Log($"[Runner] posisi pandang: {BenchmarkContext.ViewPos} yaw {BenchmarkContext.ViewYaw:F1}");
            }

            var jobs = pilot ? BuildPilotJobs() : BuildMainJobs();
            float cooldown = pilot ? pilotCooldownSec : cooldownSec;
            int done = 0, skipped = 0;

            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                string path = pilot ? BenchmarkContext.PilotPath(j.method, j.cubes)
                                    : BenchmarkContext.OutputPath(j.method, j.category, j.cubes, j.run);
                if (File.Exists(path)) { skipped++; continue; }

                BenchmarkContext.FromRunner = true;
                BenchmarkContext.Pilot      = pilot;
                BenchmarkContext.Category   = j.category;
                BenchmarkContext.CubeCount  = j.cubes;
                BenchmarkContext.RunId      = j.run;
                BenchmarkContext.WarmupSec  = pilot ? pilotWarmupSec : warmupSec;
                BenchmarkContext.MaxRows    = pilot ? pilotRows : 0;
                BenchmarkContext.Finished   = false;

                Status(null);   // sembunyikan teks selama pengukuran
                Debug.Log($"[Runner] ({i + 1}/{jobs.Count}) {j.method} {j.category} {j.cubes} cube run {j.run}");
                yield return SceneManager.LoadSceneAsync(BenchmarkContext.SceneName(j.method));
                while (!BenchmarkContext.Finished) yield return null;

                yield return SceneManager.LoadSceneAsync(RunnerScene);
                done++;

                bool last = i == jobs.Count - 1;
                for (float t = last ? 0f : cooldown; t > 0f; t -= 1f)
                {
                    Status($"Selesai {i + 1}/{jobs.Count}: {j.method} {j.category} {j.cubes} cube run {j.run}\n" +
                           $"Pendinginan {t:F0} detik...\nHz: {QuestPerfLock.CurrentHz():F0}");
                    yield return new WaitForSecondsRealtime(1f);
                }
            }

            BenchmarkContext.FromRunner = false;
            string dir = pilot ? BenchmarkContext.PilotDir : BenchmarkContext.OutputDir;
            Status($"SEMUA SELESAI ({(pilot ? "PILOT" : "UTAMA")})\n{done} run baru, {skipped} dilewati\nData: {dir}");
            Debug.Log($"[Runner] selesai. {done} run baru, {skipped} dilewati. Folder: {dir}");
        }

        List<Job> BuildPilotJobs()
        {
            var list = new List<Job>();
            foreach (int c in pilotCubeCounts)
            {
                if (includeMono) list.Add(new Job { method = BenchmarkMethod.Mono, category = "pilot", cubes = c, run = 0 });
                if (includeDots) list.Add(new Job { method = BenchmarkMethod.Dots, category = "pilot", cubes = c, run = 0 });
            }
            return list;
        }

        List<Job> BuildMainJobs()
        {
            var list = new List<Job>();
            foreach (var cat in categories)
                for (int run = 1; run <= runsPerCategory; run++)
                {
                    // counterbalancing: run ganjil Mono dulu, run genap DOTS dulu
                    var mono = new Job { method = BenchmarkMethod.Mono, category = cat.name, cubes = cat.cubes, run = run };
                    var dots = new Job { method = BenchmarkMethod.Dots, category = cat.name, cubes = cat.cubes, run = run };
                    bool monoFirst = run % 2 == 1;
                    if (monoFirst) { if (includeMono) list.Add(mono); if (includeDots) list.Add(dots); }
                    else           { if (includeDots) list.Add(dots); if (includeMono) list.Add(mono); }
                }
            return list;
        }

        void Status(string msg)
        {
            if (statusText == null) return;
            statusText.gameObject.SetActive(msg != null);
            if (msg != null) statusText.text = msg;
        }
    }
}
