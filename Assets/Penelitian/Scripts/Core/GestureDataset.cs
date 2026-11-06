using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GestureBenchmark.Core
{
    /// <summary>
    /// Dataset gesture hasil rekaman, dibaca dari TextAsset CSV.
    /// Dipakai sebagai input tersimulasi yang identik untuk Mono dan DOTS:
    /// satu baris dataset dimasukkan per satu frame (BUKAN mengikuti time_ms).
    /// </summary>
    public sealed class GestureDataset
    {
        public GestureFrameData[] Frames;
        /// <summary>true bila baris ini adalah baris pertama sebuah trial.</summary>
        public bool[] IsTrialStart;
        public int Count => Frames.Length;
        public string SourceName;

        static readonly Dictionary<string, GestureDataset> _cache = new Dictionary<string, GestureDataset>();

        /// <summary>Parse sekali lalu disimpan, supaya tiap run tidak mem-parse ulang.</summary>
        public static GestureDataset Get(TextAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset), "Dataset CSV belum diisi.");
            if (_cache.TryGetValue(asset.name, out var ds)) return ds;
            ds = Parse(asset.text);
            ds.SourceName = asset.name;
            _cache[asset.name] = ds;
            return ds;
        }

        public static GestureDataset Parse(string csv)
        {
            var lines = csv.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var header = lines[0].Trim().Split(',');
            int Col(string name)
            {
                int i = Array.IndexOf(header, name);
                if (i < 0) throw new FormatException("Kolom tidak ditemukan: " + name);
                return i;
            }

            int cTrial = Col("trial_id"), cFrame = Col("frame_index"), cTime = Col("time_ms");
            int cLx = Col("left_x"), cLy = Col("left_y"), cLz = Col("left_z");
            int cRx = Col("right_x"), cRy = Col("right_y"), cRz = Col("right_z");
            int cDist = Col("distance"), cLabel = Col("gesture_label");

            var frames = new List<GestureFrameData>(lines.Length);
            var ci = CultureInfo.InvariantCulture;
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                var p = line.Split(',');
                float F(int c) => float.Parse(p[c], NumberStyles.Float, ci);

                frames.Add(GestureFrameData.Create(
                    int.Parse(p[cTrial], ci),
                    int.Parse(p[cFrame], ci),
                    F(cTime),
                    new Vector3(F(cLx), F(cLy), F(cLz)),
                    new Vector3(F(cRx), F(cRy), F(cRz)),
                    F(cDist),
                    p[cLabel]));
            }

            var ds = new GestureDataset { Frames = frames.ToArray() };
            ds.IsTrialStart = new bool[ds.Frames.Length];
            for (int i = 0; i < ds.Frames.Length; i++)
                ds.IsTrialStart[i] = i == 0 || ds.Frames[i].trialId != ds.Frames[i - 1].trialId;
            return ds;
        }
    }
}
