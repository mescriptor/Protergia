using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ReferenceHouseTools
{
    [DisallowMultipleComponent]
    public sealed class ReferenceHouseHotspotInput : MonoBehaviour
    {
        Rect[] regions;
        Action<int> onClick;
        InputAction press;
        InputControl pressedPointer;
        int pressedRegion = -1;

        /// <summary>Hit regions use native wall pixels from the top left. Later entries are on top.</summary>
        public void Configure(Rect[] regions, Action<int> onClick)
        {
            CancelPress();
            this.regions = regions;
            this.onClick = onClick;
        }

        void OnEnable()
        {
            press = new InputAction("House hotspot", InputActionType.PassThrough);
            press.AddBinding("<Mouse>/leftButton");
            press.AddBinding("<Touchscreen>/touch*/press");
            press.performed += OnPointer;
            press.Enable();
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            press.Dispose();
            CancelPress();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) CancelPress();
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (pressedPointer?.device == device && (change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Disconnected || change == InputDeviceChange.Disabled))
                CancelPress();
        }

        void OnPointer(InputAction.CallbackContext context)
        {
            if (regions == null || onClick == null) return;
            var control = (ButtonControl)context.control;
            var touch = control.parent as TouchControl;
            Vector2 position = touch != null ? touch.position.ReadValue() : ((Mouse)control.device).position.ReadValue();

            if (control.isPressed)
            {
                if (pressedPointer != null || (touch != null && touch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Began))
                    return;

                pressedPointer = control;
                pressedRegion = HitTest(position);
                return;
            }

            if (pressedPointer != control) return;
            int released = pressedRegion;
            CancelPress();
            if (touch != null && touch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Ended) return;
            if (released >= 0 && released == HitTest(position)) onClick(released);
        }

        int HitTest(Vector2 position)
        {
            // The live output stretches its native render texture edge to edge on each axis.
            if (Screen.width <= 0 || Screen.height <= 0) return -1;
            var wallPoint = new Vector2(position.x / Screen.width * ReferenceHouseLedWall.Width,
                (1f - position.y / Screen.height) * ReferenceHouseLedWall.Height);
            for (int i = regions.Length - 1; i >= 0; i--)
                if (regions[i].Contains(wallPoint)) return i;
            return -1;
        }

        void CancelPress()
        {
            pressedPointer = null;
            pressedRegion = -1;
        }
    }
}
