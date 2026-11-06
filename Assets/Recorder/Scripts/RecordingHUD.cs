using TMPro;
using UnityEngine;
using GestureBenchmark.Core;

namespace GestureBenchmark.Recording
{
    /// <summary>
    /// Panel informasi di dalam VR.
    ///
    /// Ini bukan pelengkap — tanpa HUD, operator tidak tahu label apa yang aktif,
    /// sudah berapa trial terkumpul, dan apakah trial barusan tersimpan atau dibuang.
    /// Di dalam headset kamu tidak bisa lihat Console Unity.
    /// </summary>
    public class RecordingHUD : MonoBehaviour
    {
        [SerializeField] private GestureRecorder recorder;

        [Header("Teks")]
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text hintText;

        [Header("Target Kuota per Label")]
        [SerializeField] private int targetPerLabel = 30;

        [Header("Warna")]
        [SerializeField] private Color idleColor      = new(0.75f, 0.75f, 0.78f);
        [SerializeField] private Color recordingColor = new(0.95f, 0.35f, 0.35f);

        private void OnEnable()
        {
            if (recorder != null) recorder.OnStateChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (recorder != null) recorder.OnStateChanged -= Refresh;
        }

        private void Update()
        {
            if (recorder == null) return;

            if (recorder.IsRecording)
            {
                if (statusText != null)
                {
                    statusText.color = recordingColor;
                    statusText.text  = $"● MEREKAM   {recorder.ElapsedSeconds:F1}s";
                }
            }
            else if (statusText != null && statusText.color == recordingColor)
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            if (recorder == null) return;

            if (labelText != null)
            {
                labelText.text = $"Trial #{recorder.NextTrialId}\n" +
                                 $"<size=80%>{recorder.CurrentLabel.ToDisplayString()}</size>";
            }

            if (statusText != null)
            {
                statusText.color = idleColor;
                statusText.text  = recorder.SavedTrialCount > 0
                    ? $"Siap. Trial terakhir: {recorder.LastTrialFrameCount} frame"
                    : "Siap.";
            }

            if (progressText != null)
            {
                progressText.text =
                    $"zoom_in   {Bar(recorder.CountFor(GestureLabel.ZoomIn))}\n" +
                    $"zoom_out  {Bar(recorder.CountFor(GestureLabel.ZoomOut))}\n" +
                    $"none      {Bar(recorder.CountFor(GestureLabel.None))}\n" +
                    $"<size=75%>dibuang: {recorder.DiscardedTrialCount}</size>";
            }

            if (hintText != null)
            {
                hintText.text =
                    "Trigger kanan  : tahan untuk merekam\n" +
                    "Tombol X kiri  : ganti label\n" +
                    "Tombol Y kiri  : batalkan trial terakhir";
            }
        }

        private string Bar(int count)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(10f * count / targetPerLabel), 0, 10);
            return $"[{new string('|', filled)}{new string('.', 10 - filled)}] {count}/{targetPerLabel}";
        }
    }
}