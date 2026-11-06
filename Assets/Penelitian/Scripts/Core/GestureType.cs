namespace GestureBenchmark.Core
{
    /// <summary>
    /// Tipe gesture hasil deteksi.
    ///
    /// Nilai int-nya sengaja eksplisit karena versi DOTS nanti merepresentasikan
    /// gesture sebagai int (struct DOTS tidak boleh berisi enum managed).
    /// Nilai di sini HARUS sama dengan nilai di GestureResultData versi DOTS.
    /// </summary>
    public enum GestureType
    {
        None    = 0,
        ZoomIn  = 1,
        ZoomOut = 2
    }

    /// <summary>Label ground truth untuk satu trial. Ditentukan peneliti, bukan hasil deteksi.</summary>
    public enum GestureLabel
    {
        None    = 0,
        ZoomIn  = 1,
        ZoomOut = 2
    }

    public static class GestureLabelExtensions
    {
        /// <summary>String yang ditulis ke kolom gesture_label di CSV.</summary>
        public static string ToCsvString(this GestureLabel label) => label switch
        {
            GestureLabel.ZoomIn  => "zoom_in",
            GestureLabel.ZoomOut => "zoom_out",
            _                    => "none"
        };

        /// <summary>Teks yang ditampilkan ke responden di HUD.</summary>
        public static string ToDisplayString(this GestureLabel label) => label switch
        {
            GestureLabel.ZoomIn  => "ZOOM IN  (renggangkan tangan)",
            GestureLabel.ZoomOut => "ZOOM OUT (rapatkan tangan)",
            _                    => "NONE  (gerakan bebas / diam)"
        };
    }
}