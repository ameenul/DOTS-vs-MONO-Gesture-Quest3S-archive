"""
Grafik hasil pengujian DOTS vs MonoBehaviour.

Pakai (setelah analisis.py):
    python grafik.py <folder>

Output di <folder>/grafik/:
    boxplot_semua.png          6 panel box plot (metrik × kategori, Mono vs DOTS)
    boxplot_<metrik>.png       box plot per metrik (untuk artikel)
    interaksi.png              grafik interaksi metode × kategori (median)
    frametime_berat_run01.png  frame time per frame, kategori berat, run 1
"""
import glob
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

# palet kategorikal tervalidasi (slot 1 & 2), teks & garis netral
C = {"mono": "#2a78d6", "dots": "#eb6834"}
NAMA = {"mono": "MonoBehaviour (OOP)", "dots": "DOTS/ECS (DOP)"}
TXT, TXT2, MUTED, GRID, AXIS = "#0b0b0b", "#52514e", "#898781", "#e1e0d9", "#c3c2b7"
KAT = ["rendah", "sedang", "berat"]
KAT_LABEL = {"rendah": "Rendah\n(1.000 cube)", "sedang": "Sedang\n(4.000 cube)", "berat": "Berat\n(8.000 cube)"}
METRIK = [
    ("fps_mean", "FPS rata-rata", "FPS"),
    ("frametime_p99_ms", "Frame time p99", "ms"),
    ("logic_median_ms", "Waktu pemrosesan logika", "ms"),
    ("render_median_ms", "Waktu persiapan rendering", "ms"),
    ("response_median_ms", "Waktu respons gesture", "ms"),
    ("frametime_median_ms", "Frame time median", "ms"),
]

from matplotlib.ticker import FuncFormatter


def koma(x, nd=2):
    """Angka dengan koma desimal (format Indonesia)."""
    return f"{x:.{nd}f}".replace(".", ",")


TICK = FuncFormatter(lambda v, _: (f"{v:g}").replace(".", ","))

plt.rcParams.update({
    "font.family": "DejaVu Sans", "font.size": 10,
    "axes.edgecolor": AXIS, "axes.labelcolor": TXT2, "axes.titlecolor": TXT,
    "xtick.color": TXT2, "ytick.color": TXT2,
    "axes.spines.top": False, "axes.spines.right": False,
    "axes.grid": True, "axes.grid.axis": "y", "grid.color": GRID, "grid.linewidth": 0.6,
    "figure.facecolor": "white", "axes.facecolor": "white",
    "savefig.dpi": 300, "savefig.bbox": "tight",
})


def boxpanel(ax, s, col, title, unit, budget=None):
    pos, data, colors = [], [], []
    for i, k in enumerate(KAT):
        for j, m in enumerate(["mono", "dots"]):
            x = s[(s.kategori == k) & (s.method == m)][col].values
            pos.append(i * 3 + j)
            data.append(x)
            colors.append(C[m])
    bp = ax.boxplot(data, positions=pos, widths=0.7, patch_artist=True, showfliers=False,
                    medianprops=dict(color=TXT, linewidth=1.5),
                    whiskerprops=dict(color=MUTED, linewidth=1), capprops=dict(color=MUTED, linewidth=1))
    for patch, c in zip(bp["boxes"], colors):
        patch.set_facecolor(c + "40")
        patch.set_edgecolor(c)
        patch.set_linewidth(1.5)
    rng = np.random.default_rng(1)
    for p, x, c in zip(pos, data, colors):
        ax.scatter(p + rng.uniform(-0.15, 0.15, len(x)), x, s=16, color=c,
                   edgecolor="white", linewidth=0.6, zorder=3)
    if budget is not None:
        ax.axhline(budget, color=MUTED, linestyle="--", linewidth=1)
        ax.text(pos[-1] + 0.6, budget, " " + f"{budget:g}".replace(".", ","), color=MUTED, va="center", fontsize=8)
    ax.set_xticks([0.5, 3.5, 6.5])
    ax.set_xticklabels([KAT_LABEL[k] for k in KAT])
    ax.set_title(title, loc="left", fontsize=11, fontweight="bold")
    ax.set_ylabel(unit)
    ax.set_xlim(-0.8, 8.4)
    ax.yaxis.set_major_formatter(TICK)


def legend(fig_or_ax, **kw):
    from matplotlib.patches import Patch
    h = [Patch(facecolor=C[m] + "40", edgecolor=C[m], linewidth=1.5, label=NAMA[m]) for m in ["mono", "dots"]]
    return fig_or_ax.legend(handles=h, frameon=False, **kw)


def budget_for(col, hz):
    if col == "fps_mean":
        return hz
    if col.startswith("frametime"):
        return round(1000 / hz, 1)
    return None


def main(folder):
    s = pd.read_csv(os.path.join(folder, "ringkasan_per_run.csv"))
    s = s[s.kategori.isin(KAT)]
    hz = float(s.display_hz.iloc[0])
    out = os.path.join(folder, "grafik")
    os.makedirs(out, exist_ok=True)

    # 1. semua box plot
    fig, axes = plt.subplots(2, 3, figsize=(15, 8.5))
    for ax, (col, title, unit) in zip(axes.flat, METRIK):
        boxpanel(ax, s, col, title, unit, budget_for(col, hz))
    legend(fig, loc="upper center", ncol=2, bbox_to_anchor=(0.5, 1.02), fontsize=11)
    fig.text(0.5, -0.01, "Setiap titik = satu run (n = 10 per kelompok). Garis putus-putus: batas 72 FPS / 13,9 ms.",
             ha="center", color=TXT2, fontsize=9)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    fig.savefig(os.path.join(out, "boxplot_semua.png"))
    plt.close(fig)

    # 2. box plot per metrik
    for col, title, unit in METRIK:
        fig, ax = plt.subplots(figsize=(6.5, 4.2))
        boxpanel(ax, s, col, title, unit, budget_for(col, hz))
        legend(ax, loc="upper left", fontsize=9)
        fig.savefig(os.path.join(out, f"boxplot_{col}.png"))
        plt.close(fig)

    # 3. grafik interaksi
    fig, axes = plt.subplots(2, 3, figsize=(15, 8))
    for ax, (col, title, unit) in zip(axes.flat, METRIK):
        for m, mk in [("mono", "o"), ("dots", "s")]:
            med = [s[(s.kategori == k) & (s.method == m)][col].median() for k in KAT]
            ax.plot(range(3), med, color=C[m], linewidth=2, marker=mk, markersize=8,
                    markeredgecolor="white", markeredgewidth=1.5, label=NAMA[m])
            ax.annotate(koma(med[-1]), (2, med[-1]), xytext=(8, 0), textcoords="offset points",
                        va="center", color=TXT2, fontsize=8)
        b = budget_for(col, hz)
        if b is not None:
            ax.axhline(b, color=MUTED, linestyle="--", linewidth=1)
        ax.set_xticks(range(3))
        ax.set_xticklabels([KAT_LABEL[k] for k in KAT])
        ax.set_xlim(-0.3, 2.5)
        ax.yaxis.set_major_formatter(TICK)
        ax.set_title(title, loc="left", fontsize=11, fontweight="bold")
        ax.set_ylabel(unit)
    h, l = axes.flat[0].get_legend_handles_labels()
    fig.legend(h, l, loc="upper center", ncol=2, frameon=False, bbox_to_anchor=(0.5, 1.02), fontsize=11)
    fig.text(0.5, -0.01, "Titik = median 10 run. Garis yang tidak sejajar menunjukkan interaksi metode × kategori.",
             ha="center", color=TXT2, fontsize=9)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    fig.savefig(os.path.join(out, "interaksi.png"))
    plt.close(fig)

    # 4. frame time per frame, berat run 1
    fig, ax = plt.subplots(figsize=(12, 4))
    for m in ["mono", "dots"]:
        f = glob.glob(os.path.join(folder, f"{m}_berat_*_run01.csv"))
        if not f:
            continue
        d = pd.read_csv(f[0], comment="#")
        ft = np.diff(d.t_start_ms.values)
        t = (d.t_start_ms.values[1:] - d.t_start_ms.values[0]) / 1000
        ax.plot(t, ft, color=C[m], linewidth=0.6, label=NAMA[m], alpha=0.9)
    ax.axhline(1000 / hz, color=MUTED, linestyle="--", linewidth=1)
    ax.text(ax.get_xlim()[1], 1000 / hz, " " + koma(1000 / hz, 1) + " ms", color=MUTED, va="center", fontsize=8)
    ax.yaxis.set_major_formatter(TICK)
    ax.set_xlabel("Waktu sejak awal pemutaran dataset (detik)")
    ax.set_ylabel("Frame time (ms)")
    ax.set_title("Frame time per frame — kategori berat (8.000 cube), run 1", loc="left", fontsize=11, fontweight="bold")
    ax.legend(frameon=False, loc="upper right")
    fig.savefig(os.path.join(out, "frametime_berat_run01.png"))
    plt.close(fig)

    print("Grafik tersimpan di", out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
