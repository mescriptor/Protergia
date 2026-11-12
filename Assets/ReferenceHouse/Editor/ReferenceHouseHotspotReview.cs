using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ReferenceHouseTools
{
    public static class ReferenceHouseHotspotReview
    {
        /// <summary>Exercise the actual pointer bindings at the current Game view resolution.</summary>
        public static IEnumerator Verify(ReferenceHouseLedWall wall)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Hotspot review requires Play mode.");
            var labels = ReferenceHouseDiagram.Labels;
            var meter = labels.First(label => label.Id == ReferenceHouseDiagram.Hotspot.Meter);
            var other = labels.First(label => label.Id != ReferenceHouseDiagram.Hotspot.Meter);
            var meterPoint = ScreenPoint(meter.HitBounds);
            var otherPoint = ScreenPoint(other.HitBounds);
            int previous = wall.SelectedIndex;
            var originalMouse = Mouse.current;
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                wall.CloseDetails();
                Expect(wall, -1, "initial state");

                Gesture(mouse, meterPoint, otherPoint);
                Expect(wall, -1, "drag from the meter to another label");

                Gesture(mouse, meterPoint, meterPoint);
                Expect(wall, (int)meter.Id, "click meter");
                Gesture(mouse, meterPoint, meterPoint);
                Expect(wall, -1, "click meter again");

                Gesture(mouse, meterPoint, meterPoint);
                foreach (var label in labels.Where(label => label.Id != meter.Id))
                {
                    var point = ScreenPoint(label.HitBounds);
                    Gesture(mouse, point, point);
                    Expect(wall, (int)label.Id, "switch to " + label.Id);
                }
                Debug.Log($"HOTSPOT_INPUT_VERIFIED {Screen.width}x{Screen.height}");
                yield return null;
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                if (originalMouse != null && originalMouse.added) originalMouse.MakeCurrent();
                if (wall && wall.SelectedIndex != previous)
                {
                    wall.CloseDetails();
                    if (previous >= 0) wall.ActivateHotspot(previous);
                }
            }
        }

        static Vector2 ScreenPoint(Rect bounds)
        {
            var center = bounds.center;
            return new Vector2(center.x * Screen.width / ReferenceHouseLedWall.Width,
                Screen.height - center.y * Screen.height / ReferenceHouseLedWall.Height);
        }

        static void Gesture(Mouse mouse, Vector2 down, Vector2 up)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = down }.WithButton(MouseButton.Left));
            InputSystem.Update();
            if (down != up)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { position = up }.WithButton(MouseButton.Left));
                InputSystem.Update();
            }
            InputSystem.QueueStateEvent(mouse, new MouseState { position = up });
            InputSystem.Update();
        }

        static void Expect(ReferenceHouseLedWall wall, int expected, string step)
        {
            if (wall.SelectedIndex != expected)
                throw new InvalidOperationException($"Hotspot review at {Screen.width}x{Screen.height}: " +
                    $"{step} selected {wall.SelectedIndex}, expected {expected}.");
            for (int index = 0; index < wall.detailPanels.Length; index++)
                if (!wall.detailPanels[index] || wall.detailPanels[index].activeInHierarchy != (index == expected))
                    throw new InvalidOperationException($"Hotspot review: unexpected card visibility at {index} after {step}.");
        }
    }
}
