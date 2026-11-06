using UnityEngine;
using GestureBenchmark.Core;

namespace GestureBenchmark.Recording
{
    /// <summary>
    /// Feedback visual: satu cube membesar/mengecil saat detector mengeluarkan
    /// ZoomIn atau ZoomOut.
    ///
    /// BUKAN bagian dari pengukuran penelitian. Tujuannya semata-mata supaya
    /// responden tahu gesture-nya terbaca, sehingga gerakan mereka lebih natural
    /// dan konsisten antar trial. Nilai scale di sini tidak pernah masuk dataset.
    ///
    /// Rumus scale mengikuti dokumen rancangan:
    ///     ZoomIn  : scale += |deltaDistance| * scaleMultiplier
    ///     ZoomOut : scale -= |deltaDistance| * scaleMultiplier
    ///     scale    = Clamp(scale, minScale, maxScale)
    /// </summary>
    public class CubeScalePreview : MonoBehaviour
    {
        [SerializeField] private Transform targetObject;

        [Header("Parameter Scale")]
        [SerializeField] private float scaleMultiplier = 2f;
        [SerializeField] private float minScale = 0.5f;
        [SerializeField] private float maxScale = 3f;
        [SerializeField] private float initialScale = 1f;

        [Header("Warna Status (opsional)")]
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Color idleColor    = new(0.55f, 0.55f, 0.60f);
        [SerializeField] private Color zoomInColor  = new(0.30f, 0.80f, 0.45f);
        [SerializeField] private Color zoomOutColor = new(0.90f, 0.55f, 0.25f);

        private float _currentScale;
        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            ResetScale();
        }

        public void ResetScale()
        {
            _currentScale = initialScale;
            if (targetObject != null)
                targetObject.localScale = Vector3.one * _currentScale;
            ApplyColor(idleColor);
        }

        public void Apply(in GestureDetectionResult result)
        {
            if (targetObject == null) return;

            switch (result.type)
            {
                case GestureType.ZoomIn:
                    _currentScale += Mathf.Abs(result.deltaDistance) * scaleMultiplier;
                    ApplyColor(zoomInColor);
                    break;

                case GestureType.ZoomOut:
                    _currentScale -= Mathf.Abs(result.deltaDistance) * scaleMultiplier;
                    ApplyColor(zoomOutColor);
                    break;

                default:
                    ApplyColor(idleColor);
                    break;
            }

            _currentScale = Mathf.Clamp(_currentScale, minScale, maxScale);
            targetObject.localScale = Vector3.one * _currentScale;
        }

        private void ApplyColor(Color c)
        {
            if (targetRenderer == null) return;

            targetRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);  // URP
            _mpb.SetColor("_Color", c);      // Built-in
            targetRenderer.SetPropertyBlock(_mpb);
        }
    }
}