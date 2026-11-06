# Arsip proyek pengujian — DOTS vs MonoBehaviour (Meta Quest 3S)

Proyek Unity **asli** tempat data penelitian dihasilkan (uji pendahuluan 1 November 2026 dan pengujian utama 2 November 2026).
Repositori ini disimpan sebagai arsip apa adanya agar kondisi saat pengujian dapat ditelusuri.

> Untuk memakai, mempelajari, atau menjalankan ulang penelitian, gunakan repositori utama
> **[DOTS-vs-MONO-Gesture-Quest3S](https://github.com/ameenul/DOTS-vs-MONO-Gesture-Quest3S)**. Isinya sama untuk bagian penelitian, tetapi sudah dibersihkan dari aset template
> dan dilengkapi panduan.

## Isi

| Lokasi | Keterangan |
|---|---|
| `Assets/Penelitian/` | testbed DOTS vs MonoBehaviour, dataset gesture, 60 CSV pengujian utama, 16 CSV uji pendahuluan, skrip dan hasil analisis |
| `Assets/Recorder/` | perekam gesture (disalin dari proyek perekam) |
| `Assets/Settings/`, `Assets/XR/`, `Assets/XRI/` | pengaturan URP, OpenXR, dan XR Interaction Toolkit yang berlaku saat pengujian |

Aset bawaan template VR Unity (VRTemplateAssets, scene contoh, sampel XR Hands) dan berkas percobaan awal telah dihapus
setelah pengujian karena tidak dipakai. Isi folder `Assets` kini identik dengan repositori utama; yang berbeda hanya
pengaturan proyek (nama paket aplikasi dan dua paket eksperimen di `Packages/manifest.json`).

## Lingkungan saat pengujian

- Unity 6000.3.9f1, URP 17.3.0 (Performance URP Config, rendering path Forward), Android, IL2CPP, ARM64, Vulkan
- OpenXR 1.17.1, Meta OpenXR 2.5.1; SpaceWarp, Automatic Dynamic Resolution, dan Foveated Rendering nonaktif
- Entities 1.5.0, Entities Graphics 1.5.0, Burst 1.8.29
- Meta Quest 3S, refresh rate 72 Hz, level CPU dan GPU SustainedHigh
- Nama paket aplikasi saat pengujian: `com.DefaultCompany.VRTemplate` (setelah pengujian diganti menjadi
  `id.ac.ub.filkom.dotsvsmono.archive`; penggantian nama tidak memengaruhi data yang sudah terkumpul)

Proyek juga memuat paket `com.unity.ai.assistant` dan `com.unity.pipeline` (eksperimen) yang tidak dipakai oleh skrip penelitian.

## Penulis

Muhammad Aminul Akbar, S.Kom., M.T. (Ketua) · Ir. Issa Arwani, S.Kom., M.Sc. · Ir. Tri Afirianto, S.T., M.T.
Fakultas Ilmu Komputer, Universitas Brawijaya
