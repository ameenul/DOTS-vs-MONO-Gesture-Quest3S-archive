using UnityEngine;

namespace GestureBenchmark.Core
{
    public struct GestureDetectionResult
    {
        public GestureType type;
        public float currentDistance;
        public float deltaDistance;
        public float deltaFromStart;
    }

    /// <summary>
    /// Detector zoom berbasis aturan (rule-based).
    ///
    /// PENTING untuk validitas penelitian:
    /// Kelas ini adalah versi OOP dari algoritma. Nanti versi DOTS harus punya
    /// logika yang IDENTIK — urutan operasi, threshold, dan cara reset counter
    /// harus sama persis. Kalau ada satu baris yang beda, yang kamu ukur bukan
    /// lagi "OOP vs DOTS" tapi "algoritma A vs algoritma B".
    ///
    /// Saat RECORDING, detector ini dipakai HANYA untuk feedback visual cube.
    /// Hasilnya tidak masuk dataset. Dataset menyimpan posisi mentah saja.
    /// </summary>
    public class ZoomGestureDetector
    {
        private bool  _hasStarted;
        private float _startDistance;
        private float _previousDistance;
        private int   _consistentZoomInFrames;
        private int   _consistentZoomOutFrames;

        private readonly float _zoomThreshold;
        private readonly float _smallMovementThreshold;
        private readonly int   _minConsistentFrames;

        public ZoomGestureDetector(
            float zoomThreshold = 0.15f,
            float smallMovementThreshold = 0.005f,
            int   minConsistentFrames = 5)
        {
            _zoomThreshold          = zoomThreshold;
            _smallMovementThreshold = smallMovementThreshold;
            _minConsistentFrames    = minConsistentFrames;
        }

        /// <summary>
        /// Dipanggil di awal setiap trial. startDistance akan diambil ulang dari
        /// frame pertama setelah reset.
        /// </summary>
        public void Reset()
        {
            _hasStarted              = false;
            _startDistance           = 0f;
            _previousDistance        = 0f;
            _consistentZoomInFrames  = 0;
            _consistentZoomOutFrames = 0;
        }

        public GestureDetectionResult ProcessFrame(Vector3 leftPosition, Vector3 rightPosition)
        {
            float currentDistance = Vector3.Distance(leftPosition, rightPosition);

            // Frame pertama: hanya menetapkan baseline, belum bisa menghitung delta.
            if (!_hasStarted)
            {
                _hasStarted       = true;
                _startDistance    = currentDistance;
                _previousDistance = currentDistance;

                return new GestureDetectionResult
                {
                    type            = GestureType.None,
                    currentDistance = currentDistance,
                    deltaDistance   = 0f,
                    deltaFromStart  = 0f
                };
            }

            float deltaDistance  = currentDistance - _previousDistance;
            float deltaFromStart = currentDistance - _startDistance;

            // Hitung konsistensi arah gerakan. Counter arah berlawanan di-reset,
            // supaya gerakan bolak-balik tidak terakumulasi jadi deteksi palsu.
            if (deltaDistance > _smallMovementThreshold)
            {
                _consistentZoomInFrames++;
                _consistentZoomOutFrames = 0;
            }
            else if (deltaDistance < -_smallMovementThreshold)
            {
                _consistentZoomOutFrames++;
                _consistentZoomInFrames = 0;
            }
            // Gerakan di bawah smallMovementThreshold dianggap noise:
            // kedua counter dibiarkan, tidak naik dan tidak di-reset.

            GestureType detectedType = GestureType.None;

            if (deltaFromStart >= _zoomThreshold &&
                _consistentZoomInFrames >= _minConsistentFrames)
            {
                detectedType = GestureType.ZoomIn;
            }
            else if (deltaFromStart <= -_zoomThreshold &&
                     _consistentZoomOutFrames >= _minConsistentFrames)
            {
                detectedType = GestureType.ZoomOut;
            }

            _previousDistance = currentDistance;

            return new GestureDetectionResult
            {
                type            = detectedType,
                currentDistance = currentDistance,
                deltaDistance   = deltaDistance,
                deltaFromStart  = deltaFromStart
            };
        }
    }
}