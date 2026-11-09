REFERENCE HOUSE — V10 REFERENCE CONTRAST

Current scene: Assets/ReferenceHouse/Scenes/ReferenceHouse.unity
Current render: Blender/ReferenceHouse/Preview_Contrast_After_v10.png
Before render: Blender/ReferenceHouse/Preview_Contrast_Before_v10.png
Comparison.html shows the reference and the before/after renders.

V10 reduces frontal and room fill, deepens the basement recesses, and retains
directional window light and warm practical lights. The sun is 1.36, front
bounce 0.70, living/upper opening light 0.35/0.30, room fills 0.04 and basement
bounce 0.38. The two bounce lights now cast soft shadows, using medium shadow
resolution so the practical lights retain their full shadow resolution.
Ambient sky/equator/ground colors are lowered; localized SSAO intensity is 0.57.
Exposure and contrast remain at the V9 values, 0.04 and 12.

Only Charcoal_architectural_frame.mat changes among surface assets. It is shared
by the roof beam and the two front slab bands; its color is (0.39,0.395,0.385).
All 2,593 other material, texture, mesh and prefab files match the V10 snapshot.
Camera settings and all model transform overrides are unchanged.

Lighting was baked on the RTX 4060 with 128 direct, 1024 indirect and 512
environment samples and six bounces. A fresh Unity process restored both
directional lightmaps, 410 mapped renderers and 150 probes, then rendered the
saved scene. No shadow-atlas downscaling was reported on the final pass.
The original Unity assets and render are in Blender/ReferenceHouse/BeforeContrastV10.
Menu: Reference House > Refine reference contrast and bake (V10).
Apply V10 after older look/import menus to restore the current appearance.
The Blender source remains V8; the current look is authored in Unity.

PREVIOUS V9 — LIGHTING-ONLY REVIEW

Current scene: Assets/ReferenceHouse/Scenes/ReferenceHouse.unity
Current render: Blender/ReferenceHouse/Preview_Lighting_After_v9.png
Before render: Blender/ReferenceHouse/Preview_Lighting_Before_v9.png
Comparison.html shows the reference and switches between the two lighting passes.

V9 changes lighting only: front-right afternoon sunlight enters the open section,
cooler window daylight, reduced uniform fill, warm practical lights, localized
contact shadows, six GI bounces and higher sampling. Unity saved 128 direct,
1024 indirect and 512 environment samples, with two directional lightmaps and
410 baked renderers. Lighting was baked on the RTX 4060 and verified on reload.
All 2,594 material, texture, mesh and prefab files match their pre-V9 hashes.
The camera and model transforms are unchanged. Materials are deferred for review.
The original Unity assets are retained in Blender/ReferenceHouse/BeforeRealismV9.
Menu: Reference House > Refine lighting only and bake (V9).
Apply the V9 menu after older look/import menus to restore this lighting.
The Blender source remains V8; this lighting revision is authored in Unity.

PREVIOUS V8 — unified doors, polished stone and neighborhood backdrop

Open: Assets/ReferenceHouse/Scenes/ReferenceHouse.unity
Reusable model: Assets/ReferenceHouse/Prefabs/ReferenceHouse.prefab

The scene contains the detailed house, its furniture and appliances, rooftop
photovoltaics, car and planting. The expanded front section is preserved.
1,170 model meshes. Units are metres.
The service basement is 1.69 m deep, with its rear wall 8.5 cm behind the
washing machine. The roof array has a backing wall and sloped end return.
All four doors now share the same oak leaf, square frame, handles and aligned
wood grain: 0.934 m leaf width, 2.117 m leaf height, 44 mm thickness, with
0.94 m between jambs. The former larger entrance reveal has matching plaster infill.
The right wall courses meet their slabs; the driveway and property boundary
meet the retaining foundation at the front corner and beside the house.

Materials: dedicated shared door materials and 8 albedo/normal texture maps. Editable
URP materials are in Materials; the foliage materials are double-sided.
The two small light diffusers/LED indicators use luminous Unlit materials;
illumination comes from separate scene lights.

Lighting uses a mixed directional sun, warm ceiling and pendant lights, and
baked rectangular daylight sources at the left windows. The reference palette
warms the oak and linen, tones down the stone floor, and neutralizes the fascia.
The living and kitchen floor uses five polished stone tones, smoothness 0.89,
subtle stone normals and a 512px local box-projected reflection probe covering
the full extended floor. Ceiling and pendant lights supply direct specular
highlights; broad opening lights add soft daylight to both storeys.
Five thin transparent polish surfaces use a live mirrored camera for soft
furniture/window reflections. The stone, grout, rug and original lightmaps remain
separate. The reflection camera renders at 1536px and excludes the photo backdrop.
The camera backdrop is an image reconstructed from the reference, stored at
Textures/Neighborhood_Backdrop.png. It is excluded from reflection captures.
Unity baked three directional/color atlases on
the NVIDIA GeForce RTX 4060 Laptop GPU. A fresh editor restored lighting on 410
mesh renderers and loaded 150 baked light probes. The lightmaps and LightingData
asset are under Assets/ReferenceHouse/Scenes/ReferenceHouse. The bake settings
are in Assets/ReferenceHouse/Settings/ReferenceHouse_BakedLighting.lighting.

The scene's Rendering object selects its own URP profile when loaded. The
existing SampleScene and original PC rendering assets have been preserved.

Menus:
Reference House > Refine doors, polished floor and neighborhood
  Reapplies the complete V8 revision and bakes its lighting.
Reference House > Import detailed model and lighting
  Regenerates the house from Blender/ReferenceHouse/UnityFullV8/scene.json.
  Save any open modified scene first; the importer refuses to discard it.
  After a full rebuild, use the V8 refinement menu to restore its final look.
Reference House > Render current scene
  Renders the house camera and writes Blender/ReferenceHouse/Preview_Unity.png.
Reference House > Bake lighting with GPU
  Rebakes the current lighting. Use after changing lighting or static geometry.
Reference House > Apply reference look and bake with GPU
  Reapplies the reference material palette and daylight settings, then rebakes.

Verified in Unity 6000.3.23f1 with URP 17.3.0. The preview is an actual Unity
render, rendered at 3600 x 2700 and downsampled within Unity to 1800 x 1350.
The final camera render is Blender/ReferenceHouse/Preview_Unity_Reference_v8.png.
Comparison.html displays the reference, the new render and the previous V7
image. Preview_Unity_Floor_v8.png shows the living floor and doors more closely.
The car and
planting asset hashes and the existing scene instance transforms were verified
against the project snapshot taken before the lighting changes.
The pre-V8 Unity assets are backed up in Blender/ReferenceHouse/BeforeReferenceV8.
Reference_House.blend and Reference_House_v8.blend contain the synchronized
door/floor source update, also exported as Reference_House_Detailed.fbx;
lighting and the camera backdrop are authored in Unity. The image-generation
prompt is recorded in Blender/ReferenceHouse/Neighborhood_Backdrop.prompt.txt.
