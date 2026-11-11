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

## LED wall preview

Open the Reference House scene and choose **Reference House > LED Wall > Set up 1872x1560 and Play**. This saves an editable Canvas in the scene, selects a fixed **1872 × 1560** Game view, and enters Play. The photovoltaic label follows its scene object; the three lower panels show the same sample at **24, 36, and 48 px**, with automatic font resizing disabled. At the target resolution, the Canvas scale is 1:1.

The setup captures `output/led-wall/House_1872x1560.png` and `output/led-wall/House_LED_Panels_1872x1560.png`. Use **Reference House > LED Wall > Capture house and panels** during Play to capture again. `verification.json` records the actual resolution, Canvas scale, and text measurements. The Game view may display the image scaled to fit the editor window; use the original PNG at 100% for LED readability comparisons. Text and font sizes remain editable under **LED Wall | Canvas 1872x1560** in the scene hierarchy.
