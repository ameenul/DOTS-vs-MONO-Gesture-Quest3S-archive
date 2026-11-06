using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;

namespace GestureBenchmark.Core
{
    /// <summary>
    /// Mengunci kondisi performa Quest supaya semua run diuji pada kondisi yang sama
    /// (proyek ini memakai OpenXR + Meta OpenXR, bukan Meta XR SDK/OVRManager):
    ///   - refresh rate layar dikunci (default 72 Hz)
    ///   - foveated rendering dimatikan (level 0)
    ///   - level performa CPU & GPU: SustainedHigh (hindari perubahan clock otomatis)
    ///   - tracking origin: Floor
    /// Application SpaceWarp dan Automatic Dynamic Resolution dimatikan lewat
    /// OpenXR Feature Settings (sudah dicek oleh menu Penelitian > Setup Testbed).
    /// </summary>
    public static class QuestPerfLock
    {
        // Dua list terpisah: Apply() mengiterasi _displays, sedangkan CurrentHz() mengisi _hzDisplays.
        // (Memakai satu list yang sama membuat "Collection was modified" saat CurrentHz dipanggil di dalam loop.)
        static readonly List<XRDisplaySubsystem> _displays   = new List<XRDisplaySubsystem>();
        static readonly List<XRDisplaySubsystem> _hzDisplays = new List<XRDisplaySubsystem>();
        static readonly List<XRInputSubsystem>   _inputs     = new List<XRInputSubsystem>();

        public static void Apply(float targetHz)
        {
            SubsystemManager.GetSubsystems(_displays);
            foreach (var d in _displays)
            {
                bool ok = d.TryRequestDisplayRefreshRate(targetHz);
                d.foveatedRenderingLevel = 0f;
                Debug.Log($"[PerfLock] request {targetHz} Hz -> {ok}, sekarang {CurrentHz():F1} Hz, foveation={d.foveatedRenderingLevel}");
            }

            try
            {
                XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Cpu, PerformanceLevelHint.SustainedHigh);
                XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Gpu, PerformanceLevelHint.SustainedHigh);
                Debug.Log("[PerfLock] CPU/GPU level hint = SustainedHigh");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PerfLock] gagal set performance level: " + e.Message);
            }

            SubsystemManager.GetSubsystems(_inputs);
            foreach (var i in _inputs) i.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        }

        public static float CurrentHz()
        {
            SubsystemManager.GetSubsystems(_hzDisplays);
            foreach (var d in _hzDisplays)
                if (d.TryGetDisplayRefreshRate(out float hz)) return hz;
            return 0f;
        }
    }
}
