"""Draws docs/compare.png from the traces the head-to-head writes (run `dotnet run -c Release` here first).

    python3 chart.py
"""
import csv
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "..", "docs", "compare.png")
STYLE = [("#9a9a9a", "-", 1.6), ("#e08a2e", "-", 1.6), ("#8a5cc7", "-", 1.6), ("#1f6fd1", "-", 2.4)]


def read(name):
    with open(os.path.join(HERE, name)) as f:
        rows = list(csv.reader(f))
    names = rows[0][1:]
    t = [float(r[0]) for r in rows[1:]]
    cols = [[float(r[k + 1]) for r in rows[1:]] for k in range(len(names))]
    return names, t, cols


fig, axes = plt.subplots(1, 2, figsize=(11, 4.2), facecolor="white")
for ax, (name, title, rest, until) in zip(axes, [
        ("compare_table_tennis_trace.csv", "A table-tennis ball let go 0.5 m under water", 1.3, 3.0),
        ("compare_wooden_trace.csv", "A wooden ball dropped onto still water", 0.0, 10.0)]):
    names, t, cols = read(name)
    for (colour, ls, lw), label, ys in zip(STYLE, names, cols):
        ax.plot(t, ys, color=colour, linestyle=ls, linewidth=lw, label=label.replace(" (force on own mass)", ""))
    ax.axhline(rest, color="#555555", linewidth=0.8, linestyle=":")
    ax.text(until * 0.98, rest - 0.4, "floating height", ha="right", va="top", fontsize=8, color="#555555",
            bbox=dict(facecolor="white", edgecolor="none", pad=1.0))
    ax.set_xlim(0.0, until)
    ax.set_title(title, fontsize=11)
    ax.set_xlabel("time (s)")
    ax.set_ylabel("height of its centre (cm)")
    ax.set_facecolor("white")
    ax.grid(alpha=0.25)
axes[0].set_ylim(-10.0, 8.0)
axes[1].set_ylim(-16.0, 14.0)
axes[0].legend(fontsize=8.5, loc="lower right", frameon=False)
fig.tight_layout()
fig.savefig(OUT, dpi=110, facecolor="white")
print("wrote", os.path.normpath(OUT))
