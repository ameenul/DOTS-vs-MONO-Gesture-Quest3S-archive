using UnityEngine;
using UnityEngine.XR;

namespace GestureBenchmark.Recording
{
    /// <summary>
    /// Pembacaan input controller Meta Quest via legacy XR Input API
    /// (UnityEngine.XR.InputDevices).
    ///
    /// Dipilih karena tidak butuh setup Input Action Asset — cukup pasang komponen
    /// dan jalan. Untuk keperluan recording ini sudah memadai.
    ///
    /// Menyediakan deteksi edge (baru ditekan / baru dilepas), bukan cuma
    /// status tekan, supaya recorder bisa membedakan "mulai trial" dari
    /// "trigger masih ditahan".
    /// </summary>
    public class XRControllerInput : MonoBehaviour
    {
        [Header("Threshold")]
        [Tooltip("Nilai trigger (0-1) untuk dianggap ditekan.")]
        [SerializeField] private float triggerPressThreshold = 0.75f;

        [Tooltip("Nilai untuk dianggap dilepas. Dibuat lebih kecil dari threshold tekan " +
                 "(histeresis) supaya status tidak berkedip saat jari bergetar di perbatasan.")]
        [SerializeField] private float triggerReleaseThreshold = 0.55f;

        // --- Status frame ini ---
        public bool RightTriggerHeld   { get; private set; }
        public bool RightTriggerDown   { get; private set; }  // baru ditekan frame ini
        public bool RightTriggerUp     { get; private set; }  // baru dilepas frame ini

        public bool LeftPrimaryDown    { get; private set; }  // tombol X
        public bool LeftSecondaryDown  { get; private set; }  // tombol Y

        public bool LeftTracked        { get; private set; }
        public bool RightTracked       { get; private set; }
        public bool BothControllersValid { get; private set; }

        private bool _prevRightTrigger;
        private bool _prevLeftPrimary;
        private bool _prevLeftSecondary;

        private InputDevice _leftDevice;
        private InputDevice _rightDevice;
        private float _deviceRefreshTimer;

        private void Update()
        {
            RefreshDevicesIfNeeded();
            ReadTrigger();
            ReadButtons();
            ReadTrackingState();
        }

        /// <summary>
        /// Device bisa hilang (controller mati / sleep), jadi handle-nya
        /// di-refresh berkala, bukan di-cache sekali di Awake.
        /// </summary>
        private void RefreshDevicesIfNeeded()
        {
            _deviceRefreshTimer -= Time.unscaledDeltaTime;

            if (_leftDevice.isValid && _rightDevice.isValid && _deviceRefreshTimer > 0f)
                return;

            _leftDevice  = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            _rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            _deviceRefreshTimer = 1f;
        }

        private void ReadTrigger()
        {
            float value = 0f;
            if (_rightDevice.isValid)
                _rightDevice.TryGetFeatureValue(CommonUsages.trigger, out value);

            // Histeresis: ambang masuk dan keluar berbeda
            bool held = _prevRightTrigger
                ? value >= triggerReleaseThreshold
                : value >= triggerPressThreshold;

            RightTriggerDown = held && !_prevRightTrigger;
            RightTriggerUp   = !held && _prevRightTrigger;
            RightTriggerHeld = held;

            _prevRightTrigger = held;
        }

        private void ReadButtons()
        {
            bool primary = false, secondary = false;

            if (_leftDevice.isValid)
            {
                _leftDevice.TryGetFeatureValue(CommonUsages.primaryButton,   out primary);
                _leftDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out secondary);
            }

            LeftPrimaryDown   = primary   && !_prevLeftPrimary;
            LeftSecondaryDown = secondary && !_prevLeftSecondary;

            _prevLeftPrimary   = primary;
            _prevLeftSecondary = secondary;
        }

        private void ReadTrackingState()
        {
            bool l = false, r = false;

            if (_leftDevice.isValid)  _leftDevice.TryGetFeatureValue(CommonUsages.isTracked,  out l);
            if (_rightDevice.isValid) _rightDevice.TryGetFeatureValue(CommonUsages.isTracked, out r);

            LeftTracked  = l;
            RightTracked = r;
            BothControllersValid = _leftDevice.isValid && _rightDevice.isValid && l && r;
        }
    }
}