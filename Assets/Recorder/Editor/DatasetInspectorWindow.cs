#if UNITY_EDITOR

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GestureBenchmark.EditorTools
{
    /// <summary>
    /// Verifikasi dataset hasil recording.
    /// Buka lewat: Tools > Gesture Benchmark > Dataset Inspector
    ///
    /// PAKAI INI setelah setiap sesi, selagi responden masih ada. Kalau datanya
    /// bermasalah, masih bisa diulang. Kalau baru ketahuan seminggu kemudian,
    /// kamu harus mengundang orangnya lagi.
    /// </summary>
    public class DatasetInspectorWindow : EditorWindow
    {
        private string  _path = "";
        private string  _report = "";
        private Vector2 _scroll;

        [MenuItem("Tools/Gesture Benchmark/Dataset Inspector")]
        public static void Open() => GetWindow<DatasetInspectorWindow>("Dataset Inspector");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Verifikasi Dataset CSV", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                _path = EditorGUILayout.TextField("File CSV", _path);
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    string p = EditorUtility.OpenFilePanel("Pilih dataset", "", "csv");
                    if (!string.IsNullOrEmpty(p)) _path = p;
                }
            }

            EditorGUILayout.HelpBox(
                "Tarik file dari headset dengan:\n" +
                "adb shell ls /sdcard/Android/data/<package>/files/Dataset\n" +
                "adb pull /sdcard/Android/data/<package>/files/Dataset ./Dataset",
                MessageType.Info);

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_path)))
            {
                if (GUILayout.Button("Analisis", GUILayout.Height(28)))
                    _report = Analyze(_path);
            }

            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------

        private class Trial
        {
            public int    Id;
            public string Label;
            public int    FrameCount;
            public float  DurationMs;
            public float  StartDistance;
            public float  EndDistance;
            public float  MinDistance = float.MaxValue;
            public float  MaxDistance = float.MinValue;
            public float  MaxGapMs;
        }

        private static string Analyze(string path)
        {
            if (!File.Exists(path)) return "File tidak ditemukan.";

            var inv = CultureInfo.InvariantCulture;
            var trials = new Dictionary<int, Trial>();
            var invalidIds = LoadInvalidIds(path);

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (System.Exception e) { return "Gagal membaca file:\n" + e.Message; }

            if (lines.Length < 2) return "File kosong atau hanya berisi header.";

            int badRows = 0;
            float prevTime = 0f;
            int   prevTrial = -1;

            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].Split(',');
                if (c.Length < 11) { badRows++; continue; }

                if (!int.TryParse(c[0], out int trialId) ||
                    !float.TryParse(c[2], NumberStyles.Float, inv, out float timeMs) ||
                    !float.TryParse(c[9], NumberStyles.Float, inv, out float dist))
                {
                    badRows++; continue;
                }

                if (!trials.TryGetValue(trialId, out var t))
                {
                    t = new Trial { Id = trialId, Label = c[10].Trim(), StartDistance = dist };
                    trials[trialId] = t;
                }

                if (trialId == prevTrial)
                {
                    float gap = timeMs - prevTime;
                    if (gap > t.MaxGapMs) t.MaxGapMs = gap;
                }

                t.FrameCount++;
                t.DurationMs  = timeMs;
                t.EndDistance = dist;
                if (dist < t.MinDistance) t.MinDistance = dist;
                if (dist > t.MaxDistance) t.MaxDistance = dist;

                prevTime  = timeMs;
                prevTrial = trialId;
            }

            return BuildReport(path, trials, invalidIds, badRows);
        }

        private static HashSet<int> LoadInvalidIds(string csvPath)
        {
            var set = new HashSet<int>();
            string p = Path.ChangeExtension(csvPath, ".invalid.txt");
            if (!File.Exists(p)) return set;

            foreach (var line in File.ReadAllLines(p))
                if (int.TryParse(line.Trim(), out int id)) set.Add(id);

            return set;
        }

        private static string BuildReport(
            string path, Dictionary<int, Trial> trials, HashSet<int> invalidIds, int badRows)
        {
            var sb = new StringBuilder();
            var perLabel = new Dictionary<string, int>();
            int warnings = 0;

            sb.AppendLine("=== RINGKASAN DATASET ===");
            sb.AppendLine(Path.GetFileName(path));
            sb.AppendLine($"Total trial   : {trials.Count}");
            sb.AppendLine($"Ditandai batal: {invalidIds.Count}");
            if (badRows > 0) sb.AppendLine($"Baris rusak   : {badRows}  <-- PERIKSA");
            sb.AppendLine();

            sb.AppendLine("--- PER TRIAL ---");
            sb.AppendLine("id    label      frame  durasi   d_awal d_akhir  delta   gap_max");

            var ids = new List<int>(trials.Keys);
            ids.Sort();

            foreach (int id in ids)
            {
                var t = trials[id];
                if (invalidIds.Contains(id)) continue;

                perLabel.TryGetValue(t.Label, out int n);
                perLabel[t.Label] = n + 1;

                float delta = t.EndDistance - t.StartDistance;

                // Cek apakah arah perubahan jarak sesuai label
                string flag = "";
                if (t.Label == "zoom_in"  && delta < 0.05f) { flag = " <-- jarak tidak membesar"; warnings++; }
                if (t.Label == "zoom_out" && delta > -0.05f) { flag = " <-- jarak tidak mengecil"; warnings++; }
                if (t.FrameCount < 60)    { flag += " <-- frame sedikit"; warnings++; }
                if (t.MaxGapMs > 40f)     { flag += $" <-- ada hitch {t.MaxGapMs:F0}ms"; warnings++; }

                sb.AppendLine(
                    $"{t.Id,-5} {t.Label,-10} {t.FrameCount,5}  {t.DurationMs,6:F0}ms  " +
                    $"{t.StartDistance,6:F3} {t.EndDistance,6:F3} {delta,7:F3}  {t.MaxGapMs,5:F0}ms{flag}");
            }

            sb.AppendLine();
            sb.AppendLine("--- JUMLAH PER LABEL ---");
            foreach (var kv in perLabel)
                sb.AppendLine($"  {kv.Key,-10}: {kv.Value}");

            sb.AppendLine();
            sb.AppendLine(warnings == 0
                ? "Tidak ada peringatan. Dataset terlihat sehat."
                : $"{warnings} peringatan. Periksa baris bertanda <--.");

            sb.AppendLine();
            sb.AppendLine("PANDUAN PEMBACAAN:");
            sb.AppendLine("  delta positif besar  -> zoom_in wajar");
            sb.AppendLine("  delta negatif besar  -> zoom_out wajar");
            sb.AppendLine("  delta ~0 pada none   -> wajar");
            sb.AppendLine("  frame ~200 untuk trial 3 detik @72 FPS");
            sb.AppendLine("  gap_max jauh di atas 14ms menandakan frame drop saat rekaman");

            return sb.ToString();
        }
    }
}

#endif