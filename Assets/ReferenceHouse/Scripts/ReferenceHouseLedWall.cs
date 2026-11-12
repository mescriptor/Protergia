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

        void OnEnable()
        {
            CloseDetails();
            var labels = ReferenceHouseDiagram.Labels;
            var regions = new Rect[labels.Count];
            for (int i = 0; i < regions.Length; ++i) regions[i] = labels[i].HitBounds;
            GetComponent<ReferenceHouseHotspotInput>().Configure(regions, ActivateHotspot);
        }

        void OnDisable()
        {
            GetComponent<ReferenceHouseHotspotInput>().Configure(null, null);
            CloseDetails();
        }

        /// <summary>A second press closes the same card. Call on Unity's main thread.</summary>
        public void ActivateHotspot(int index)
        {
            if (detailPanels == null || index < 0 || index >= detailPanels.Length || !detailPanels[index]) return;
            bool open = SelectedIndex != index;
            CloseDetails();
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
            if (detailPanels != null)
                foreach (var panel in detailPanels)
                    if (panel && panel.activeSelf) panel.SetActive(false);
            SelectedIndex = -1;
        }
    }
}
