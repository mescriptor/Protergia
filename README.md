# Protergia

Unity project with the Reference House scene.

## Open the project

1. Clone this repository.
2. In Unity Hub, add the repository's root folder as a project.
3. Open it with Unity **6000.3.23f1** and allow the initial asset import to finish.
4. Open `Assets/ReferenceHouse/Scenes/ReferenceHouse.unity`.

## Project files

- `Assets`, `Packages`, and `ProjectSettings`: the main Unity project.
- `Tools`: publishing scripts. These use local Blender exports and a local `UnityBuild` staging project, which are not included in this repository or needed to open the main Unity scene.

The local `Blender` folder and duplicate `Tools/UnityBuild` project are excluded from Git, along with Unity caches, local editor settings, logs, generated solution files, and automatic Blender backups. Unity recreates its caches when the project is opened. Keep Unity `.meta` files together with their assets.

## Live LED wall output

Open the Reference House scene and choose **Reference House > LED Wall > Play live 16x9 (1920x1080)**. Unity renders the house, video and enabled LED panels together at the wall's native **1872 × 1560**, then stretches the entire frame to **1920 × 1080**. The Windows player uses this feed in fullscreen. Set the LED processor to map the entire 16:9 input edge to edge across the 1872 × 1560 wall. A circle looks wide on the operator's monitor and round on the wall.

## Interactive energy diagram

Open the Reference House scene and press **Play**. The Energy Corner starts with attract videos. **P** simulates a visitor and reveals the house; **S** simulates departure; **F2** returns presence control to the real sensor. Click an equipment label or its gold sun, or use **1–5** (top row or numpad), to open a card. The same button closes it and another switches products. **Esc** closes the current card and **F1** opens maintenance diagnostics. Physical serial buttons use the same controller as keyboard and pointer input.

Product order is **1 meter, 2 heat pump, 3 EV charger, 4 photovoltaics, 5 battery**. The meter uses the supplied artwork; heat pump, charger and PV cards reproduce the supplied Home Corner copy and conditions. The battery uses original introductory copy. The house returns to videos after 15 seconds without presence or button activity; cards close after 30 seconds. Settings and the video playlist are under `Assets/StreamingAssets/` (or the built player's `EnergyCorner_Data/StreamingAssets/`).

The selected equipment receives an amber outline on the object itself; changing or closing cards clears the previous object's highlight. Outlines are calibrated from the equipment meshes through the fixed wall camera, including each of the ten PV modules. If the camera or equipment moves, choose **Reference House > Energy Corner > Refresh object highlights**; rebuilding the diagram also refreshes them. **Verify equipment highlights** checks the actual rendered outlines and saves native previews under `output/energy-corner/` without creating a player build.

The **Photovoltaics** card shows six diagonal rays from the sky striking the exposed panel surfaces. The **Battery** card shows six bright energy pulses along the **panels → battery** cable. Meter, EV charger and heat pump show that charging leg, then continue **battery → selected equipment**. Cable pulses launch 0.18 seconds apart and travel at 780 native pixels per second. The effects repeat while the card is open and stop when the card closes or the attract videos return. **Verify equipment highlights** checks the sunlight and cable effects in Play mode and saves `Flow_*` captures alongside the highlight previews.

The diagram is calibrated to the scene’s fixed camera and the physical **1872 × 1560** wall. The meter artwork is placed at **356 × 672 native pixels**: its 712 × 1344 texture is sampled at 2:1, giving an effective type size of **20 px bold** and **17.5 px regular**. The wall canvas renders at scale **1**; the 16:9 monitor shows a stretched copy of the completed frame. Mapping that copy back onto the full LED wall restores the native proportions. To judge type size on a monitor, open a native capture at 100%.

External buttons are connected through the Energy Corner Pico serial protocol. `ReferenceHouseLedWall.ActivateMeter()` and `Activate(ReferenceHouseDiagram.Hotspot)` also route through the session controller on Unity's main thread. A device is not required for keyboard testing. The firmware currently has four physical buttons; all five products work from the keyboard.

Choose **Reference House > Energy Corner > Apply integration** to rebuild the cards and wire the input/controller/video components into the existing scene. **Verify integration** exercises the common input and state paths and writes a report and captures under `output/energy-corner/`. **Build Windows player** creates `Builds/EnergyCorner/EnergyCorner.exe` with its accompanying data folder. Standalone uses the .NET Framework API profile and Mono for `System.IO.Ports`. See [the technician handoff](docs/EnergyCorner-Handoff.md) for wiring-side responsibilities, protocol, keys, configuration and installation checks.

Choose **Reference House > LED Wall > Set up 1872x1560 and Play** to rebuild the saved canvas and capture it. **Capture house and meter** checks clicks in native mode and saves closed/open views to `output/led-wall/House_Energy_1872x1560.png` and `House_Meter_1872x1560.png`. **Verify live 16x9 buttons** checks the live output and saves `House_Meter_1920x1080.png`. The corresponding `meter-native.json` and `meter-live.json` record the measured native card bounds. Card copy is defined in `Assets/ReferenceHouse/Editor/ReferenceHouseProductCards.cs`; rebuilding recreates the generated canvas and rebinds the Energy Corner controls.
