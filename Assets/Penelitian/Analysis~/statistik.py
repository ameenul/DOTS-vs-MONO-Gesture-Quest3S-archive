"""
Analisis statistik inferensial untuk rumusan masalah penelitian DOTS vs MonoBehaviour.

Pakai (setelah analisis.py dijalankan pada folder yang sama):
    python statistik.py <folder>

Input : ringkasan_per_run.csv   (satu baris per run, hasil analisis.py)
Output: hasil_statistik.xlsx    (semua tabel)
        tabel_*.csv             (tabel yang sama dalam CSV)

Satuan sampel = run (n = 10 per kelompok metode × kategori).

RM1  Apakah DOTS lebih baik daripada Mono?
     -> per kategori: Mann-Whitney U (dua sisi), ukuran efek rank-biserial r,
        selisih median; uji t Welch sebagai analisis sensitivitas;
        koreksi Holm atas seluruh uji RM1.
RM2  Apakah jumlah cube memengaruhi FPS dan waktu respons pada kedua pendekatan?
     -> per metode: Kruskal-Wallis antar 3 kategori + epsilon²,
        post hoc Mann-Whitney berpasangan dengan koreksi Holm.
RM3  Pada kategori beban mana keuntungan DOTS teramati / belum berarti?
     -> ANOVA dua arah Aligned Rank Transform (ART; Wobbrock dkk., 2011):
        efek metode, kategori, dan interaksi metode × kategori;
        dilengkapi ringkasan keuntungan per kategori (CPU vs yang dirasakan pengguna).
"""
import os
import sys

import numpy as np
import pandas as pd
from scipy import stats
import statsmodels.formula.api as smf
from statsmodels.stats.anova import anova_lm
from statsmodels.stats.multitest import multipletests

ALPHA = 0.05
KATEGORI = ["rendah", "sedang", "berat"]
METODE = ["mono", "dots"]
METRIK = {
    "fps_mean": ("FPS rata-rata", "FPS"),
    "frametime_median_ms": ("Frame time (median)", "ms"),
    "frametime_p99_ms": ("Frame time (p99)", "ms"),
    "logic_median_ms": ("Waktu pemrosesan logika", "ms"),
    "render_median_ms": ("Waktu persiapan rendering", "ms"),
    "response_median_ms": ("Waktu respons gesture", "ms"),
}


def label(m):
    return METRIK[m][0]


# ----------------------------------------------------------------------------- deskriptif

def deskriptif(s):
    rows = []
    for k in KATEGORI:
        for me in METODE:
            g = s[(s.kategori == k) & (s.method == me)]
            for m in METRIK:
                x = g[m].values
                rows.append(dict(kategori=k, metode=me, metrik=label(m), n=len(x),
                                 mean=x.mean(), sd=x.std(ddof=1), median=np.median(x),
                                 q1=np.percentile(x, 25), q3=np.percentile(x, 75),
                                 min=x.min(), max=x.max()))
    return pd.DataFrame(rows)


# ----------------------------------------------------------------------------- uji asumsi

def _sw(x):
    return np.nan if np.ptp(x) == 0 else stats.shapiro(x).pvalue


def asumsi(s):
    rows = []
    for k in KATEGORI:
        for m in METRIK:
            a = s[(s.kategori == k) & (s.method == "mono")][m].values
            b = s[(s.kategori == k) & (s.method == "dots")][m].values
            pa, pb = _sw(a), _sw(b)
            lev = stats.levene(a, b, center="median").pvalue if np.ptp(np.r_[a, b]) > 0 else np.nan
            rows.append(dict(kategori=k, metrik=label(m),
                             shapiro_p_mono=pa, shapiro_p_dots=pb,
                             normal=bool(pa > ALPHA and pb > ALPHA),
                             levene_p=lev, varians_homogen=bool(lev > ALPHA)))
    return pd.DataFrame(rows)


# ----------------------------------------------------------------------------- RM1

def rm1(s):
    rows = []
    for k in KATEGORI:
        for m in METRIK:
            a = s[(s.kategori == k) & (s.method == "mono")][m].values
            b = s[(s.kategori == k) & (s.method == "dots")][m].values
            n1, n2 = len(a), len(b)
            u, p = stats.mannwhitneyu(a, b, alternative="two-sided")
            r_rb = 2 * u / (n1 * n2) - 1           # >0 : Mono cenderung lebih besar
            t, pw = stats.ttest_ind(a, b, equal_var=False)
            sp = np.sqrt((a.var(ddof=1) + b.var(ddof=1)) / 2)
            d = (a.mean() - b.mean()) / sp if sp > 0 else np.nan
            rows.append(dict(kategori=k, metrik=label(m), satuan=METRIK[m][1],
                             median_mono=np.median(a), median_dots=np.median(b),
                             selisih_median=np.median(a) - np.median(b),
                             U=u, p_mw=p, r_rank_biserial=r_rb,
                             t_welch=t, p_welch=pw, cohen_d=d))
    df = pd.DataFrame(rows)
    df["p_mw_holm"] = multipletests(df.p_mw, method="holm")[1]
    df["signifikan"] = df.p_mw_holm < ALPHA
    df["efek"] = pd.cut(df.r_rank_biserial.abs(), [-1, 0.1, 0.3, 0.5, 1.01],
                        labels=["sangat kecil", "kecil", "sedang", "besar"])
    return df


# ----------------------------------------------------------------------------- RM2

def rm2(s):
    kw_rows, ph_rows = [], []
    pairs = [("rendah", "sedang"), ("sedang", "berat"), ("rendah", "berat")]
    for me in METODE:
        for m in METRIK:
            groups = [s[(s.method == me) & (s.kategori == k)][m].values for k in KATEGORI]
            allx = np.concatenate(groups)
            n = len(allx)
            if np.ptp(allx) == 0:
                h, p = np.nan, np.nan
            else:
                h, p = stats.kruskal(*groups)
            eps2 = h / ((n ** 2 - 1) / (n + 1)) if not np.isnan(h) else np.nan
            kw_rows.append(dict(metode=me, metrik=label(m), H=h, df=2, p=p, epsilon2=eps2,
                                median_rendah=np.median(groups[0]),
                                median_sedang=np.median(groups[1]),
                                median_berat=np.median(groups[2])))
            ps = []
            for k1, k2 in pairs:
                a = s[(s.method == me) & (s.kategori == k1)][m].values
                b = s[(s.method == me) & (s.kategori == k2)][m].values
                u, pp = stats.mannwhitneyu(a, b, alternative="two-sided")
                ps.append(pp)
                ph_rows.append(dict(metode=me, metrik=label(m), pasangan=f"{k1} vs {k2}",
                                    median_1=np.median(a), median_2=np.median(b),
                                    U=u, p=pp, r_rank_biserial=2 * u / (len(a) * len(b)) - 1))
            adj = multipletests(ps, method="holm")[1]
            for i, v in enumerate(adj):
                ph_rows[-3 + i]["p_holm"] = v
    kw = pd.DataFrame(kw_rows)
    kw["p_holm"] = multipletests(kw.p.fillna(1), method="holm")[1]
    kw["signifikan"] = kw.p_holm < ALPHA
    ph = pd.DataFrame(ph_rows)
    ph["signifikan"] = ph.p_holm < ALPHA
    return kw, ph


# ----------------------------------------------------------------------------- RM3 (ART ANOVA)

def art_anova(df, y):
    """ANOVA dua arah Aligned Rank Transform untuk desain seimbang 2 (metode) × 3 (kategori)."""
    d = df[["method", "kategori", y]].copy()
    d.columns = ["A", "B", "y"]
    gm = d.y.mean()
    cell = d.groupby(["A", "B"]).y.transform("mean")
    ma = d.groupby("A").y.transform("mean")
    mb = d.groupby("B").y.transform("mean")
    resid = d.y - cell
    aligned = {
        "A": resid + (ma - gm),
        "B": resid + (mb - gm),
        "A:B": resid + (cell - ma - mb + gm),
    }
    names = {"A": "C(A)", "B": "C(B)", "A:B": "C(A):C(B)"}
    out = []
    for eff, al in aligned.items():
        d["r"] = stats.rankdata(al)
        model = smf.ols("r ~ C(A) * C(B)", data=d).fit()
        tab = anova_lm(model, typ=2)
        row = tab.loc[names[eff]]
        ss_err = tab.loc["Residual", "sum_sq"]
        out.append(dict(efek={"A": "metode", "B": "kategori", "A:B": "metode × kategori"}[eff],
                        F=row["F"], df1=int(row["df"]), df2=int(tab.loc["Residual", "df"]),
                        p=row["PR(>F)"], eta2_parsial=row["sum_sq"] / (row["sum_sq"] + ss_err)))
    return out


def rm3(s, r1):
    rows = []
    for m in METRIK:
        for o in art_anova(s, m):
            rows.append(dict(metrik=label(m), **o))
    art = pd.DataFrame(rows)
    art["signifikan"] = art.p < ALPHA

    # ringkasan keuntungan per kategori
    hz = s.display_hz.iloc[0] if "display_hz" in s else 72.0
    budget = 1000.0 / hz
    summ = []
    for k in KATEGORI:
        t = r1[r1.kategori == k].set_index("metrik")
        mono = s[(s.kategori == k) & (s.method == "mono")]
        cpu = bool(t.loc[label("logic_median_ms"), "signifikan"] and t.loc[label("render_median_ms"), "signifikan"])
        fps_mono = mono.fps_mean.median()
        p99_mono = mono.frametime_p99_ms.median()
        dirasakan = bool(t.loc[label("fps_mean"), "signifikan"] and fps_mono < 0.99 * hz) or \
                    bool(t.loc[label("response_median_ms"), "signifikan"])
        summ.append(dict(
            kategori=k,
            keuntungan_cpu_signifikan=cpu,
            selisih_logika_ms=t.loc[label("logic_median_ms"), "selisih_median"],
            selisih_rendering_ms=t.loc[label("render_median_ms"), "selisih_median"],
            fps_median_mono=fps_mono,
            fps_median_dots=s[(s.kategori == k) & (s.method == "dots")].fps_mean.median(),
            frametime_p99_mono=p99_mono,
            p99_mono_melewati_anggaran=bool(p99_mono > budget * 1.10),
            selisih_respons_ms=t.loc[label("response_median_ms"), "selisih_median"],
            keuntungan_dirasakan_pengguna=dirasakan,
        ))
    return art, pd.DataFrame(summ)


# ----------------------------------------------------------------------------- main

def main(folder):
    path = os.path.join(folder, "ringkasan_per_run.csv")
    if not os.path.exists(path):
        print("Jalankan dulu: python analisis.py", folder)
        return
    s = pd.read_csv(path)
    s = s[s.kategori.isin(KATEGORI)].copy()

    tabel = {
        "deskriptif": deskriptif(s),
        "asumsi": asumsi(s),
    }
    tabel["RM1_mono_vs_dots"] = rm1(s)
    tabel["RM2_kruskal"], tabel["RM2_posthoc"] = rm2(s)
    tabel["RM3_art_anova"], tabel["RM3_ringkasan"] = rm3(s, tabel["RM1_mono_vs_dots"])

    xlsx = os.path.join(folder, "hasil_statistik.xlsx")
    with pd.ExcelWriter(xlsx) as w:
        for name, t in tabel.items():
            t.to_excel(w, sheet_name=name[:31], index=False)
            t.to_csv(os.path.join(folder, f"tabel_{name}.csv"), index=False)

    pd.set_option("display.width", 250)
    pd.set_option("display.max_columns", 30)
    for name, t in tabel.items():
        if name == "deskriptif":
            continue
        print(f"\n=== {name} ===")
        print(t.round(4).to_string(index=False))
    print("\nTersimpan:", xlsx)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
