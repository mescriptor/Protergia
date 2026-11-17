using System;
using UnityEngine;

namespace ReferenceHouseTools
{
    /// <summary>Controls the information cards on the native LED canvas.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ReferenceHouseHotspotInput))]
    public sealed class ReferenceHouseLedWall : MonoBehaviour
    {
        public const int Width = 1872, Height = 1560;

        // Indexed in diagram label order, all initially inactive in the saved scene.
        public GameObject[] detailPanels;
        public int SelectedIndex { get; private set; } = -1;
        Action<ReferenceHouseDiagram.Hotspot> activationRequested;
        Action closeRequested;

        /// <summary>Route user actions through the session controller when installed.</summary>
        public void BindActions(Action<ReferenceHouseDiagram.Hotspot> activate, Action close)
        {
            activationRequested = activate;
            closeRequested = close;
        }

        void OnEnable()
        {
            HideDetails();
            var labels = ReferenceHouseDiagram.Labels;
            var regions = new Rect[labels.Count];
            for (int i = 0; i < regions.Length; ++i) regions[i] = labels[i].HitBounds;
            GetComponent<ReferenceHouseHotspotInput>().Configure(regions, ActivateHotspot);
        }

        void OnDisable()
        {
            GetComponent<ReferenceHouseHotspotInput>().Configure(null, null);
            HideDetails();
        }

        /// <summary>A second press closes the same card. Call on Unity's main thread.</summary>
        public void ActivateHotspot(int index)
        {
            if (detailPanels == null || index < 0 || index >= detailPanels.Length || !detailPanels[index]) return;
            if (activationRequested != null)
            {
                activationRequested(ReferenceHouseDiagram.Labels[index].Id);
                return;
            }
            bool open = SelectedIndex != index;
            HideDetails();
            if (open)
            {
                detailPanels[index].SetActive(true);
                SelectedIndex = index;
            }
        }

        /// <summary>Entry point for a physical button or UnityEvent; independent of the mouse.</summary>
        public void Activate(ReferenceHouseDiagram.Hotspot id)
        {
            var labels = ReferenceHouseDiagram.Labels;
            for (int i = 0; i < labels.Count; ++i)
                if (labels[i].Id == id) { ActivateHotspot(i); return; }
        }

        public void ActivateMeter() => Activate(ReferenceHouseDiagram.Hotspot.Meter);

        public void CloseDetails()
        {
            if (closeRequested != null) closeRequested();
            else HideDetails();
        }

        /// <summary>Render controller state without toggling or generating another input.</summary>
        public void ShowDetails(ReferenceHouseDiagram.Hotspot id)
        {
            var labels = ReferenceHouseDiagram.Labels;
            HideDetails();
            for (int i = 0; i < labels.Count; i++)
                if (labels[i].Id == id && detailPanels != null && i < detailPanels.Length && detailPanels[i])
                {
                    detailPanels[i].SetActive(true);
                    SelectedIndex = i;
                    return;
                }
        }

        public void HideDetails()
        {
            if (detailPanels != null)
                foreach (var panel in detailPanels)
                    if (panel && panel.activeSelf) panel.SetActive(false);
            SelectedIndex = -1;
        }
    }
}
