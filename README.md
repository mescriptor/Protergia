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
