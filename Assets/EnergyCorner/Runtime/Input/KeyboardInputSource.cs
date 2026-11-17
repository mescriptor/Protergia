using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EnergyCorner
{
    /// <summary>
    /// Dev / fallback input with the new Input System:
    ///   P = presence on, S = presence off, 1..N (top row or numpad) = buttons.
    /// P/S select simulated presence; F2 restores the sensor. Esc closes the selected card.
    /// Disable with "keyboardInput": false in config.json.
    /// </summary>
    public class KeyboardInputSource : MonoBehaviour
    {
        public event Action<bool> OnPresenceChanged;
        public event Action<int> OnButton;
        public event Action OnUseSensor;
        public event Action OnCloseProduct;

        void Update()
        {
            var cfg = EnergyCornerConfig.Current;
            var kb = Keyboard.current;
            if (kb == null || !cfg.keyboardInput) return;

            if (kb.f2Key.wasPressedThisFrame) OnUseSensor?.Invoke();
            if (kb.escapeKey.wasPressedThisFrame) OnCloseProduct?.Invoke();

            if (kb.pKey.wasPressedThisFrame) { Debug.Log("[EC] Key P (presence on)"); OnPresenceChanged?.Invoke(true); }
            if (kb.sKey.wasPressedThisFrame) { Debug.Log("[EC] Key S (presence off)"); OnPresenceChanged?.Invoke(false); }

            for (int n = 1; n <= cfg.buttonCount && n <= 9; n++)
            {
                var digit = kb[(Key)((int)Key.Digit1 + n - 1)];
                var numpad = kb[(Key)((int)Key.Numpad1 + n - 1)];
                if (digit.wasPressedThisFrame || numpad.wasPressedThisFrame)
                {
                    Debug.Log("[EC] Key " + n + " (button)");
                    OnButton?.Invoke(n);
                }
            }
        }
    }
}
