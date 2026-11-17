using System;
using EnergyCorner;
using UnityEngine;

namespace ReferenceHouseTools
{
    /// <summary>Maps the technician's product numbers to the existing house view.</summary>
    [DisallowMultipleComponent]
    public sealed class ReferenceHouseEnergyCornerBridge : MonoBehaviour
    {
        public EnergyCornerController controller;
        public ReferenceHouseLedWall wall;
        public CanvasGroup interactiveLayer;
        public ReferenceHouseDiagram diagram;
        public AttractVideoPlayer video;
        public DebugOverlay debugOverlay;

        public static ReferenceHouseDiagram.Hotspot ProductToHotspot(int product)
        {
            switch (product)
            {
                case 1: return ReferenceHouseDiagram.Hotspot.Meter;
                case 2: return ReferenceHouseDiagram.Hotspot.HeatPump;
                case 3: return ReferenceHouseDiagram.Hotspot.Charger;
                case 4: return ReferenceHouseDiagram.Hotspot.Photovoltaics;
                case 5: return ReferenceHouseDiagram.Hotspot.Battery;
                default: throw new ArgumentOutOfRangeException(nameof(product), product, "Expected product 1 to 5.");
            }
        }

        public static int HotspotToProduct(ReferenceHouseDiagram.Hotspot hotspot)
        {
            switch (hotspot)
            {
                case ReferenceHouseDiagram.Hotspot.Meter: return 1;
                case ReferenceHouseDiagram.Hotspot.HeatPump: return 2;
                case ReferenceHouseDiagram.Hotspot.Charger: return 3;
                case ReferenceHouseDiagram.Hotspot.Photovoltaics: return 4;
                case ReferenceHouseDiagram.Hotspot.Battery: return 5;
                default: throw new ArgumentOutOfRangeException(nameof(hotspot));
            }
        }

        void OnEnable()
        {
            if (!controller || !wall) return;
            wall.BindActions(Activate, controller.CloseProduct);
            wall.GetComponent<ReferenceHouseHotspotInput>().CanInteract = CanUsePointer;
            controller.onEnterAttract.AddListener(EnterAttract);
            controller.onEnterInteractive.AddListener(EnterInteractive);
            controller.onExitInteractive.AddListener(ExitInteractive);
            controller.onEnterProduct.AddListener(ShowProduct);
            controller.onExitProduct.AddListener(HideProduct);
            SyncView();
        }

        void OnDisable()
        {
            if (wall)
            {
                wall.BindActions(null, null);
                wall.GetComponent<ReferenceHouseHotspotInput>().CanInteract = null;
            }
            if (!controller) return;
            controller.onEnterAttract.RemoveListener(EnterAttract);
            controller.onEnterInteractive.RemoveListener(EnterInteractive);
            controller.onExitInteractive.RemoveListener(ExitInteractive);
            controller.onEnterProduct.RemoveListener(ShowProduct);
            controller.onExitProduct.RemoveListener(HideProduct);
        }

        bool CanUsePointer() => controller && controller.State != CornerState.Attract &&
            (!debugOverlay || !debugOverlay.BlocksInteraction);

        void Activate(ReferenceHouseDiagram.Hotspot id) => controller.HandleButton(HotspotToProduct(id));
        void EnterAttract() { SetHouseVisible(false); HideProduct(0); }
        void EnterInteractive() => SetHouseVisible(true);
        void ExitInteractive() => SetHouseVisible(false);

        void SetHouseVisible(bool visible)
        {
            if (!interactiveLayer) return;
            interactiveLayer.alpha = visible ? 1f : 0f;
            interactiveLayer.interactable = visible;
            interactiveLayer.blocksRaycasts = visible;
        }

        void ShowProduct(int product)
        {
            wall.ShowDetails(ProductToHotspot(product));
            if (diagram) diagram.SetSelected(wall.SelectedIndex);
        }

        void HideProduct(int unused)
        {
            if (wall) wall.HideDetails();
            if (diagram) diagram.SetSelected(-1);
        }

        void SyncView()
        {
            SetHouseVisible(controller.State != CornerState.Attract);
            if (controller.State == CornerState.Product) ShowProduct(controller.CurrentProduct);
            else HideProduct(0);
        }
    }
}
