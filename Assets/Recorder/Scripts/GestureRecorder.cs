using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using GestureBenchmark.Core;

namespace GestureBenchmark.Recording
{
    /// <summary>
    /// Merekam posisi controller kiri-kanan per frame selama trigger ditekan,
    /// lalu menulis satu trial ke file CSV sesi.
    ///
    /// Satu tekanan trigger = satu trial.
    ///
    /// PERBEDAAN PENTING DARI DOKUMEN RANCANGAN:
    /// Dokumen menulis satu file CSV per trial dengan nama
    /// gesture_trial_{id}_{label}.csv. Itu berarti ~90 file untuk eksperimen utama,
    /// dan file akan SALING MENIMPA kalau trialId terulang antar responden.
    /// Di sini semua trial satu responden ditulis ke SATU file sesi, di-append
    /// tiap trial selesai (jadi tetap aman kalau aplikasi crash di tengah sesi).
    /// Kolom CSV tetap identik dengan dokumen.
    /// </summary>
    public class GestureRecorder : MonoBehaviour
    {
        // ============================================================
        // Inspector
        // ============================================================

        [Header("Referensi")]
        [SerializeField] private XRControllerInput controllerInput;
        [SerializeField] private Transform leftController;
        [SerializeField] private Transform rightController;

        [Tooltip("XR Origin. Posisi direkam relatif terhadap ini supaya data tidak " +
                 "bergantung di mana responden berdiri. Biarkan kosong untuk pakai world space.")]
        [SerializeField] private Transform xrOrigin;

        [Header("Identitas Sesi")]
        [Tooltip("Kode responden, bukan nama asli. Contoh: P01, P02.")]
        [SerializeField] private string participantId = "P01";

        [Header("Trial")]
        [SerializeField] private GestureLabel currentLabel = GestureLabel.ZoomIn;
        [SerializeField] private int nextTrialId = 1;

        [Tooltip("Durasi maksimum satu trial (detik). Trial berhenti otomatis " +
                 "kalau trigger belum dilepas sampai batas ini.")]
        [SerializeField] private float maxTrialDuration = 3f;

        [Tooltip("Trial dengan frame lebih sedikit dari ini dibuang (dianggap salah tekan).")]
        [SerializeField] private int minValidFrames = 20;

        [Header("Preview (tidak masuk dataset)")]
        [SerializeField] private CubeScalePreview cubePreview;
        [SerializeField] private float zoomThreshold = 0.15f;
        [SerializeField] private float smallMovementThreshold = 0.005f;
        [SerializeField] private int minConsistentFrames = 5;

        [Header("Editor Fallback")]
        [Tooltip("Tombol R/S di keyboard, hanya untuk uji coba di Unity Editor.")]
        [SerializeField] private bool enableKeyboardFallback = true;

        [Tooltip("Izinkan merekam di Editor walau controller tidak terdeteksi. " +
         "Hanya untuk menguji pipeline — data yang dihasilkan tidak valid.")]
        [SerializeField] private bool allowUntrackedInEditor = true;

        private bool IsEditorBypass => Application.isEditor && allowUntrackedInEditor;
        // ============================================================
        // State
        // ============================================================

        private readonly List<GestureFrameData> _frames = new(512);
        private ZoomGestureDetector _detector;

        private bool _isRecording;
        private double _trialStartTime;
        private int _frameIndex;
        private int _trackingLossFrames;

        private string _sessionFilePath;
        private readonly StringBuilder _sb = new(1024);

        // Statistik sesi, ditampilkan di HUD
        public int SavedTrialCount { get; private set; }
        public int DiscardedTrialCount { get; private set; }
        public int LastTrialFrameCount { get; private set; }

        public bool IsRecording => _isRecording;
        public GestureLabel CurrentLabel => currentLabel;
        public int NextTrialId => nextTrialId;
        public string ParticipantId => participantId;
        public string SessionFilePath => _sessionFilePath;

        public double ElapsedSeconds =>
            _isRecording ? Time.unscaledTimeAsDouble - _trialStartTime : 0.0;

        /// <summary>Jumlah trial tersimpan per label, untuk memantau progres kuota.</summary>
        private readonly Dictionary<GestureLabel, int> _countPerLabel = new()
        {
            { GestureLabel.ZoomIn, 0 }, { GestureLabel.ZoomOut, 0 }, { GestureLabel.None, 0 }
        };

        public int CountFor(GestureLabel label) => _countPerLabel[label];

        public event Action OnStateChanged;

        // ============================================================
        // Lifecycle
        // ============================================================

        private void Awake()
        {
            _detector = new ZoomGestureDetector(
                zoomThreshold, smallMovementThreshold, minConsistentFrames);

            CreateSessionFile();
        }

        private void Update()
        {
            HandleInput();

            if (!_isRecording) return;

            // Batas durasi trial
            if (Time.unscaledTimeAsDouble - _trialStartTime >= maxTrialDuration)
            {
                StopRecording();
                return;
            }

            RecordCurrentFrame();
        }

        // ============================================================
        // Input
        // ============================================================

        private void HandleInput()
        {
            if (controllerInput != null)
            {
                if (controllerInput.RightTriggerDown) StartRecording();
                if (controllerInput.RightTriggerUp) StopRecording();

                // Tombol X kiri: ganti label trial berikutnya
                if (controllerInput.LeftPrimaryDown && !_isRecording) CycleLabel();

                // Tombol Y kiri: batalkan trial terakhir yang tersimpan
                if (controllerInput.LeftSecondaryDown && !_isRecording) UndoLastTrial();
            }

#if UNITY_EDITOR
            if (enableKeyboardFallback)
            {
                if (Input.GetKeyDown(KeyCode.R)) StartRecording();
                if (Input.GetKeyDown(KeyCode.S)) StopRecording();
                if (Input.GetKeyDown(KeyCode.L)) CycleLabel();
            }
#endif
        }

        public void CycleLabel()
        {
            currentLabel = currentLabel switch
            {
                GestureLabel.ZoomIn => GestureLabel.ZoomOut,
                GestureLabel.ZoomOut => GestureLabel.None,
                _ => GestureLabel.ZoomIn
            };
            OnStateChanged?.Invoke();
        }

        public void SetLabel(GestureLabel label)
        {
            currentLabel = label;
            OnStateChanged?.Invoke();
        }

        // ============================================================
        // Kontrol Trial
        // ============================================================

        public void StartRecording()
        {
            if (_isRecording) return;

            if (leftController == null || rightController == null)
            {
                Debug.LogError("[GestureRecorder] Transform controller belum di-assign.");
                return;
            }

            if (controllerInput != null && !controllerInput.BothControllersValid)
            {
                bool bypass = Application.isEditor && allowUntrackedInEditor;

                if (!bypass)
                {
                    Debug.LogWarning("[GestureRecorder] Controller tidak terlacak. Trial dibatalkan.");
                    return;
                }

                Debug.LogWarning("[GestureRecorder] MODE UJI EDITOR — controller tidak terlacak. " +
                                 "Data trial ini TIDAK VALID untuk penelitian.");
            }

            _frames.Clear();
            _frameIndex = 0;
            _trackingLossFrames = 0;
            _trialStartTime = Time.unscaledTimeAsDouble;
            _isRecording = true;

            // Reset detector: startDistance akan diambil dari frame pertama trial ini.
            _detector.Reset();
            cubePreview?.ResetScale();

            OnStateChanged?.Invoke();
        }

        public void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;

            LastTrialFrameCount = _frames.Count;

            if (_frames.Count < minValidFrames)
            {
                DiscardedTrialCount++;
                Debug.LogWarning($"[GestureRecorder] Trial dibuang: hanya {_frames.Count} frame " +
                                 $"(minimum {minValidFrames}).");
                OnStateChanged?.Invoke();
                return;
            }

            if (_trackingLossFrames > _frames.Count * 0.1f)
            {
                DiscardedTrialCount++;
                Debug.LogWarning($"[GestureRecorder] Trial dibuang: tracking hilang di " +
                                 $"{_trackingLossFrames}/{_frames.Count} frame.");
                OnStateChanged?.Invoke();
                return;
            }

            AppendTrialToCsv();

            SavedTrialCount++;
            _countPerLabel[currentLabel]++;
            nextTrialId++;

            Debug.Log($"[GestureRecorder] Trial {nextTrialId - 1} tersimpan | " +
                      $"{currentLabel.ToCsvString()} | {_frames.Count} frame");

            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Menandai trial terakhir sebagai tidak valid. CSV tidak diubah (append-only);
        /// trial yang dibatalkan dicatat di file .invalid.txt untuk difilter saat analisis.
        /// </summary>
        public void UndoLastTrial()
        {
            if (SavedTrialCount == 0) return;

            int invalidTrialId = nextTrialId - 1;
            string invalidPath = Path.ChangeExtension(_sessionFilePath, ".invalid.txt");
            File.AppendAllText(invalidPath, invalidTrialId + Environment.NewLine);

            SavedTrialCount--;
            _countPerLabel[currentLabel] = Mathf.Max(0, _countPerLabel[currentLabel] - 1);

            Debug.Log($"[GestureRecorder] Trial {invalidTrialId} ditandai tidak valid.");
            OnStateChanged?.Invoke();
        }

        // ============================================================
        // Perekaman per frame
        // ============================================================

        private void RecordCurrentFrame()
        {
            Vector3 left = ToRecordingSpace(leftController.position);
            Vector3 right = ToRecordingSpace(rightController.position);

            if (controllerInput != null && !controllerInput.BothControllersValid && !IsEditorBypass)
                _trackingLossFrames++;

            float timeMs = (float)((Time.unscaledTimeAsDouble - _trialStartTime) * 1000.0);
            float distance = Vector3.Distance(left, right);

            // Detector dijalankan HANYA untuk feedback visual. Hasilnya tidak disimpan.
            var result = _detector.ProcessFrame(left, right);
            cubePreview?.Apply(result);

            _frames.Add(GestureFrameData.Create(
                nextTrialId, _frameIndex, timeMs, left, right, distance,
                currentLabel.ToCsvString()));

            _frameIndex++;
        }

        private Vector3 ToRecordingSpace(Vector3 worldPosition)
            => xrOrigin != null ? xrOrigin.InverseTransformPoint(worldPosition) : worldPosition;

        // ============================================================
        // CSV
        // ============================================================

        private void CreateSessionFile()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Dataset");
            Directory.CreateDirectory(dir);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _sessionFilePath = Path.Combine(dir, $"{participantId}_{stamp}.csv");

            File.WriteAllText(_sessionFilePath,
                "trial_id,frame_index,time_ms," +
                "left_x,left_y,left_z," +
                "right_x,right_y,right_z," +
                "distance,gesture_label" + Environment.NewLine);

            WriteSessionMetadata(stamp);

            Debug.Log($"[GestureRecorder] File sesi: {_sessionFilePath}");
        }

        private void AppendTrialToCsv()
        {
            _sb.Clear();
            var inv = CultureInfo.InvariantCulture;

            foreach (var f in _frames)
            {
                _sb.Append(f.trialId).Append(',')
                   .Append(f.frameIndex).Append(',')
                   .Append(f.timeMs.ToString("F3", inv)).Append(',')
                   .Append(f.leftX.ToString("F6", inv)).Append(',')
                   .Append(f.leftY.ToString("F6", inv)).Append(',')
                   .Append(f.leftZ.ToString("F6", inv)).Append(',')
                   .Append(f.rightX.ToString("F6", inv)).Append(',')
                   .Append(f.rightY.ToString("F6", inv)).Append(',')
                   .Append(f.rightZ.ToString("F6", inv)).Append(',')
                   .Append(f.distance.ToString("F6", inv)).Append(',')
                   .Append(f.gestureLabel)
                   .Append('\n');
            }

            // Ditulis tiap trial selesai (bukan di akhir sesi) supaya data tidak
            // hilang kalau aplikasi crash atau baterai habis di tengah sesi.
            File.AppendAllText(_sessionFilePath, _sb.ToString());
        }

        private void WriteSessionMetadata(string stamp)
        {
            var meta = new SessionMetadata
            {
                participantId = participantId,
                sessionStamp = stamp,
                recordedAt = DateTime.Now.ToString("o"),
                deviceModel = SystemInfo.deviceModel,
                deviceName = SystemInfo.deviceName,
                operatingSystem = SystemInfo.operatingSystem,
                unityVersion = Application.unityVersion,
                isEditor = Application.isEditor,
                recordingSpace = xrOrigin != null ? "xr_origin_local" : "world",
                maxTrialDuration = maxTrialDuration,
                zoomThreshold = zoomThreshold,
                smallMovementThreshold = smallMovementThreshold,
                minConsistentFrames = minConsistentFrames
            };

            File.WriteAllText(
                Path.ChangeExtension(_sessionFilePath, ".meta.json"),
                JsonUtility.ToJson(meta, true));
        }

        [Serializable]
        private class SessionMetadata
        {
            public string participantId;
            public string sessionStamp;
            public string recordedAt;
            public string deviceModel;
            public string deviceName;
            public string operatingSystem;
            public string unityVersion;
            public bool isEditor;
            public string recordingSpace;
            public float maxTrialDuration;
            public float zoomThreshold;
            public float smallMovementThreshold;
            public int minConsistentFrames;
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause && _isRecording) StopRecording();
        }
    }
}