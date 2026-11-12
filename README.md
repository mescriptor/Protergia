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

Open the Reference House scene and press **Play**. Click an equipment label or its gold sun to open the corresponding card. Click the same button again to close it, or choose another to switch cards. The **Μετρητής** button beside the kitchen opens the supplied “Smart Meters: Οφέλη και δυνατότητες” artwork. The other four cards have short Greek descriptions for the demo.

The diagram is calibrated to the scene’s fixed camera and the physical **1872 × 1560** wall. The meter artwork is placed at **356 × 672 native pixels**: its 712 × 1344 texture is sampled at 2:1, giving an effective type size of **20 px bold** and **17.5 px regular**. The wall canvas renders at scale **1**; the 16:9 monitor shows a stretched copy of the completed frame. Mapping that copy back onto the full LED wall restores the native proportions. To judge type size on a monitor, open a native capture at 100%.

For future external buttons, call `ReferenceHouseLedWall.ActivateMeter()` or `Activate(ReferenceHouseDiagram.Hotspot)` from Unity’s main thread. Mouse and touch use the same activation logic. No Arduino or serial connection is required for this preview.

Choose **Reference House > LED Wall > Set up 1872x1560 and Play** to rebuild the saved canvas from the diagram and capture it. **Capture house and meter** checks clicks in native mode and saves closed/open views to `output/led-wall/House_Energy_1872x1560.png` and `House_Meter_1872x1560.png`. **Verify live 16x9 buttons** checks the live output and saves `House_Meter_1920x1080.png`. The corresponding `meter-native.json` and `meter-live.json` record the measured native card bounds. Card copy for the other equipment is editable under **LED Wall | Canvas 1872x1560**.
