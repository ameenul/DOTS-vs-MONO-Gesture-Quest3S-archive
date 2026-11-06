"""
Analisis hasil benchmark DOTS vs Mono.

Pakai:
    python analisis.py <folder_berisi_csv>            # pengujian utama
    python analisis.py --pilot <folder_Pilot>         # uji pendahuluan (kalibrasi kategori)

Folder CSV diambil dari Quest dengan:
    adb pull /sdcard/Android/data/<package>/files/Penelitian  ./data

Output (di folder yang sama):
    ringkasan_per_run.csv   satu baris per run  -> bahan uji beda
    respons_per_trial.csv   satu baris per trial zoom per run
    validasi_deteksi.csv    cek frame deteksi Mono == DOTS
    uji_beda.csv            Mann-Whitney U Mono vs DOTS per jumlah cube (butuh scipy)

Definisi metrik:
    frame time (ms)     = selisih t_start_ms antar frame berurutan (lama satu frame utuh)
    FPS                 = 1000 / frame time
    logic_ms            = waktu pemrosesan logika (Stopwatch, fase Update)
    render_ms           = waktu persiapan rendering main thread (Stopwatch, PreLateUpdate..PostLateUpdate)
    (kolom cpu_main_ms/gpu_ms dari FrameTimingManager tidak dipakai: di Quest/OpenXR
     cpu_main termasuk waktu menunggu XR dan gpu_ms selalu 0)
    waktu respons (ms)  = dari awal frame pemicu (deteksi pertama di trial)
                          sampai akhir frame tempat SEMUA cube pertama kali berubah skala
                          (= t_start frame berikutnya)
"""
import glob
import os
import sys

import numpy as np
import pandas as pd


def load(path):
    meta = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            if not line.startswith("#"):
                break
            k, _, v = line[1:].strip().partition("=")
            meta[k.strip()] = v.strip()
    df = pd.read_csv(path, comment="#")
    return meta, df


def summarize(meta, df, hz):
    budget = 1000.0 / hz
    ft = np.diff(df.t_start_ms.values)          # frame time tiap frame (ms)
    logic = df.logic_ms.values
    total_s = (df.t_start_ms.values[-1] - df.t_start_ms.values[0]) / 1000.0
    return {
        "kategori": meta.get("kategori", ""),
        "method": meta["method"],
        "cubes": int(meta["cubes"]),
        "run": int(meta["run"]),
        "display_hz": hz,
        "frames": len(df),
        "fps_mean": (len(df) - 1) / total_s,
        "frametime_median_ms": np.median(ft),
        "frametime_p95_ms": np.percentile(ft, 95),
        "frametime_p99_ms": np.percentile(ft, 99),
        "pct_frame_terlambat": float(np.mean(ft > budget * 1.5) * 100),
        "logic_median_ms": np.median(logic),
        "logic_mean_ms": np.mean(logic),
        "logic_p95_ms": np.percentile(logic, 95),
        "logic_p99_ms": np.percentile(logic, 99),
        "render_median_ms": np.nanmedian(df.render_ms.values) if "render_ms" in df else np.nan,
        "render_p95_ms": np.nanpercentile(df.render_ms.values, 95) if "render_ms" in df else np.nan,
    }


def responses(meta, df):
    rows = []
    t = df.t_start_ms.values
    for tid, g in df.groupby("trial_id", sort=False):
        label = g.label.iloc[0]
        if label == "none":
            continue
        idx = g.index.values
        det = g.index[g.detected.values != 0]
        if len(det) == 0:
            rows.append(dict(kategori=meta.get("kategori", ""), method=meta["method"], cubes=int(meta["cubes"]), run=int(meta["run"]),
                             trial_id=tid, label=label, trigger_row=np.nan, frames_delay=np.nan,
                             response_ms=np.nan, onsets=0))
            continue
        trig = det[0]
        after = df.loc[trig:idx[-1]]
        ch = after.index[after.all_scaled.values == 1]
        onsets = int(((g.detected.values != 0) & (np.r_[0, g.detected.values[:-1]] == 0)).sum())
        if len(ch) == 0 or ch[0] + 1 >= len(df):
            resp, delay = np.nan, np.nan
        else:
            c = ch[0]
            resp = t[c + 1] - t[trig]
            delay = c - trig
        rows.append(dict(kategori=meta.get("kategori", ""), method=meta["method"], cubes=int(meta["cubes"]), run=int(meta["run"]),
                         trial_id=tid, label=label, trigger_row=int(df.row[trig]),
                         frames_delay=delay, response_ms=resp, onsets=onsets))
    return rows


ORDER = {"rendah": 0, "sedang": 1, "berat": 2}


def main(folder):
    # hanya file mentah (mono_/dots_), bukan file hasil olahan seperti ringkasan_per_run.csv
    files = sorted(glob.glob(os.path.join(folder, "mono_*_run*.csv")) +
                   glob.glob(os.path.join(folder, "dots_*_run*.csv")))
    if not files:
        print("Tidak ada file CSV di", folder)
        return
    summ, resp, triggers = [], [], {}
    for f in files:
        meta, df = load(f)
        hz = float(meta.get("display_hz") or 0) or 72.0
        s = summarize(meta, df, hz)
        r = responses(meta, df)
        rr = pd.DataFrame(r)
        s["response_median_ms"] = rr.response_ms.median()
        s["response_mean_ms"] = rr.response_ms.mean()
        s["response_frames_delay_max"] = rr.frames_delay.max()
        summ.append(s)
        resp += r
        key = (meta.get("kategori", ""), int(meta["cubes"]), int(meta["run"]))
        triggers[(meta["method"],) + key] = rr.set_index("trial_id").trigger_row

    summ = pd.DataFrame(summ)
    summ["_o"] = summ.kategori.map(ORDER).fillna(9)
    summ = summ.sort_values(["_o", "cubes", "method", "run"]).drop(columns="_o")
    resp = pd.DataFrame(resp)
    summ.to_csv(os.path.join(folder, "ringkasan_per_run.csv"), index=False)
    resp.to_csv(os.path.join(folder, "respons_per_trial.csv"), index=False)

    # validasi: frame pemicu Mono harus sama dengan DOTS pada run yang sama
    val = []
    for (m, k, c, run), trig in triggers.items():
        if m != "mono" or ("dots", k, c, run) not in triggers:
            continue
        other = triggers[("dots", k, c, run)]
        same = (trig.fillna(-1) == other.reindex(trig.index).fillna(-1))
        val.append(dict(kategori=k, cubes=c, run=run, trials=len(trig), identik=int(same.sum()),
                        beda=",".join(map(str, trig.index[~same].tolist()))))
    val = pd.DataFrame(val)
    val.to_csv(os.path.join(folder, "validasi_deteksi.csv"), index=False)

    print("\n=== Ringkasan (median antar run) ===")
    cols = ["fps_mean", "frametime_median_ms", "frametime_p99_ms", "logic_median_ms", "render_median_ms", "response_median_ms"]
    print(summ.groupby(["kategori", "cubes", "method"], sort=False)[cols].median().round(3).to_string())
    if len(val):
        print("\n=== Validasi frame deteksi Mono vs DOTS ===")
        print(val.to_string(index=False))

    try:
        from scipy.stats import mannwhitneyu
    except ImportError:
        print("\n(scipy tidak terpasang, uji beda dilewati: pip install scipy)")
        return
    tests = []
    for (k, c), g in summ.groupby(["kategori", "cubes"], sort=False):
        for metric in ["logic_median_ms", "render_median_ms", "frametime_median_ms", "fps_mean", "response_median_ms"]:
            a = g[g.method == "mono"][metric].dropna()
            b = g[g.method == "dots"][metric].dropna()
            if len(a) < 2 or len(b) < 2:
                continue
            row = dict(kategori=k, cubes=c, metric=metric, n_mono=len(a), n_dots=len(b),
                       median_mono=a.median(), median_dots=b.median())
            if a.nunique() == 1 and b.nunique() == 1 and a.iloc[0] == b.iloc[0]:
                row.update(U=np.nan, p=np.nan, catatan="nilai identik, tidak diuji")
            else:
                u, p = mannwhitneyu(a, b, alternative="two-sided")
                row.update(U=u, p=p, catatan="")
            tests.append(row)
    tests = pd.DataFrame(tests)
    tests.to_csv(os.path.join(folder, "uji_beda.csv"), index=False)
    print("\n=== Uji Mann-Whitney U (Mono vs DOTS) per kategori ===")
    print(tests.round(4).to_string(index=False))


def pilot(folder):
    """Ringkas hasil pilot dan usulkan jumlah cube untuk kategori rendah/sedang/berat.

    Kriteria (anggaran frame B = 1000/Hz, mis. 13,9 ms di 72 Hz), dilihat pada versi Mono
    karena Mono yang lebih dulu tertekan:
        berat  : jumlah cube terkecil dengan FPS rata-rata < 95% Hz   (FPS sudah turun)
        sedang : jumlah cube terbesar di bawah 'berat' yang FPS-nya masih >= 99% Hz
                 tetapi frame time p99 > 1,1 B                        (mulai tersendat)
        rendah : jumlah cube terbesar dengan frame time p99 <= 1,05 B (benar-benar longgar)
    Usulan ini hanya titik awal; keputusan akhir tetap pada peneliti.
    """
    files = sorted(glob.glob(os.path.join(folder, "pilot_*.csv")))
    if not files:
        print("Tidak ada file pilot_*.csv di", folder)
        return
    rows = []
    for f in files:
        meta, df = load(f)
        hz = float(meta.get("display_hz") or 0) or 72.0
        rows.append(summarize(meta, df, hz))
    t = pd.DataFrame(rows).sort_values(["cubes", "method"])
    hz = t.display_hz.iloc[0]
    budget = 1000.0 / hz
    cols = ["fps_mean", "frametime_p99_ms", "pct_frame_terlambat", "logic_median_ms", "render_median_ms"]
    wide = t.pivot(index="cubes", columns="method", values=cols)
    wide.to_csv(os.path.join(folder, "ringkasan_pilot.csv"))
    print(f"=== Hasil pilot (Hz={hz:.0f}, anggaran frame={budget:.1f} ms) ===")
    print(wide.round(2).to_string())

    m = t[t.method == "mono"].set_index("cubes").sort_index()
    if m.empty:
        return
    drop = m[m.fps_mean < 0.95 * hz]
    berat = drop.index.min() if len(drop) else None
    below = m[m.index < berat] if berat is not None else m
    sedang_c = below[(below.fps_mean >= 0.99 * hz) & (below.frametime_p99_ms > 1.10 * budget)]
    sedang = sedang_c.index.max() if len(sedang_c) else None
    rendah_c = m[m.frametime_p99_ms <= 1.05 * budget]
    rendah = rendah_c.index.max() if len(rendah_c) else None
    print("\n=== Usulan kategori (berdasarkan versi Mono) ===")
    print("rendah :", rendah if rendah is not None else "-")
    print("sedang :", sedang if sedang is not None else "- (belum ada jumlah cube yang mulai tersendat)")
    print("berat  :", berat if berat is not None else "- (FPS Mono belum turun; tambah jumlah cube di pilot)")


if __name__ == "__main__":
    args = sys.argv[1:]
    if args and args[0] == "--pilot":
        pilot(args[1] if len(args) > 1 else ".")
    else:
        main(args[0] if args else ".")
