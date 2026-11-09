using System;
using UnityEditor;
using UnityEngine;

namespace ReferenceHouseTools
{
    public static class ReferenceHouseMaterialLook
    {
        private const string Root = "Assets/ReferenceHouse";

        // Colours are sRGB swatches, matching URP's Inspector colour fields.
        private readonly struct Surface
        {
            public readonly string Name;
            public readonly Color Tint;
            public readonly float Smoothness;
            public readonly float Metallic;
            public readonly string TextureFamily;
            public readonly float Tiling;
            public readonly float Relief;

            public Surface(string name, int rgb, float smoothness, float metallic = 0f,
                string textureFamily = null, float tiling = 1f, float relief = 0f)
            {
                Name = name;
                Tint = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                Smoothness = smoothness;
                Metallic = metallic;
                TextureFamily = textureFamily;
                Tiling = tiling;
                Relief = relief;
            }
        }

        private static readonly Surface[] Surfaces =
        {
            // Mineral finishes. The five existing tile materials keep their slight shade variation.
            new Surface("Warm_ivory_mineral_plaster", 0xBFB8AC, .15f, 0f, "plaster", 1.8f, .12f),
            new Surface("Interior_warm_white", 0xDED4C1, .10f, 0f, "plaster", 1.8f, .10f),
            new Surface("Detail___warm_painted_trim", 0xDDD5C6, .28f),
            new Surface("Charcoal_architectural_frame", 0x79756E, .31f, .08f),
            new Surface("Exposed_concrete___slab", 0xAEA99F, .19f, 0f, "plaster", 1.6f, .14f),
            new Surface("Limestone_retaining_walls", 0xAEA491, .16f, 0f, "stone", 1.4f, .16f),
            new Surface("Pale_limestone_coping", 0xCDC5B3, .22f, 0f, "stone", 1.4f, .12f),
            new Surface("Travertine_porcelain_00", 0xBFB29F, .23f, 0f, "stone", 1.35f, .10f),
            new Surface("Travertine_porcelain_01", 0xC2B5A2, .23f, 0f, "stone", 1.35f, .10f),
            new Surface("Travertine_porcelain_02", 0xC5B8A4, .23f, 0f, "stone", 1.35f, .10f),
            new Surface("Travertine_porcelain_03", 0xC8BBA7, .23f, 0f, "stone", 1.35f, .10f),
            new Surface("Travertine_porcelain_04", 0xCBBEAA, .23f, 0f, "stone", 1.35f, .10f),
            new Surface("Blockout___pale_stone_worktop", 0xD4CCBA, .35f, 0f, "stone", 1.35f, .08f),

            // Bare oak, sealed aprons and warmer recessed cabinet fronts.
            new Surface("Natural_oak_doors_and_stair_treads", 0xA48159, .35f, 0f, "wood", 1.2f, .18f),
            new Surface("Blockout___upper_kitchen_units", 0x987B59, .31f, 0f, "wood", 1.2f, .14f),
            new Surface("Blockout___kitchen_base_units", 0xCCC3AE, .29f),
            new Surface("Furniture___natural_light_oak", 0xAC8F6B, .34f, 0f, "wood", 1.15f, .16f),
            new Surface("Furniture___oak_end_grain_and_recess", 0x826849, .26f, 0f, "wood", 1.3f, .17f),
            new Surface("Mass___warm_wood_furniture", 0xAA8D69, .29f, 0f, "wood", 1.15f, .15f),

            // Light linen, lighter cotton sheets, and an olive-grey woven throw.
            new Surface("Furniture___ivory_woven_linen", 0xDCD5C5, .06f, 0f, "linen", 1.7f, .15f),
            new Surface("Furniture___ivory_seam_piping", 0xC9BFAE, .08f),
            new Surface("Furniture___warm_cotton_bedding", 0xEAE4D6, .06f, 0f, "linen", 1.7f, .10f),
            new Surface("Furniture___olive_grey_woven_throw", 0x918D7D, .06f, 0f, "linen", 1.7f, .18f),
            new Surface("Furniture___soft_taupe_upholstery", 0xA39785, .06f, 0f, "linen", 1.7f, .16f),
            new Surface("Mass___muted_floor_rug", 0xB0A792, .04f, 0f, "linen", 1.9f, .18f),

            // Enamel and steel read differently under the same service-room light.
            new Surface("Blockout___appliance_warm_white", 0xDBDAD2, .32f),
            new Surface("Blockout___battery_charcoal", 0x676A67, .27f),
            new Surface("Blockout___neutral_metal", 0xA1A39D, .43f, .48f),
            new Surface("Detail___brushed_stainless_steel", 0xB8BAB6, .49f, .82f),
            new Surface("Detail___satin_chrome_fittings", 0xC8C9C5, .70f, .90f),
            new Surface("Detail___rubber_appliance_seals", 0x353734, .14f),
            new Surface("Detail___ceramic_basin_interior", 0xC3C5BF, .58f, .02f)
        };

        [MenuItem("Tools/Reference House/Apply Interior and Shell Finishes")]
        public static void Apply()
        {
            int edited = 0;
            foreach (var surface in Surfaces)
                if (ApplySurface(surface)) edited++;

            Debug.Log($"Reference House finishes: {edited} material assets updated.");
        }

        private static bool ApplySurface(Surface surface)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{surface.Name}.mat");
            if (!material || !material.HasProperty("_BaseColor") || !material.HasProperty("_Smoothness"))
                throw new InvalidOperationException($"Expected a URP Lit material for {surface.Name}.");

            bool tintChanged = material.GetColor("_BaseColor") != surface.Tint;
            bool finishChanged = !Mathf.Approximately(material.GetFloat("_Smoothness"), surface.Smoothness)
                || !Mathf.Approximately(material.GetFloat("_Metallic"), surface.Metallic);
            bool mapsChanged = ApplyMaps(material, surface);
            if (!tintChanged && !finishChanged && !mapsChanged) return false;

            material.SetColor("_BaseColor", surface.Tint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", surface.Tint);
            material.SetFloat("_Smoothness", surface.Smoothness);
            material.SetFloat("_Metallic", surface.Metallic);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return true;
        }

        private static bool ApplyMaps(Material material, Surface surface)
        {
            if (surface.TextureFamily == null) return false;

            string prefix = $"{Root}/Textures/{surface.TextureFamily}";
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_neutral_albedo.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_normal.png");
            if (!albedo || !normal) throw new InvalidOperationException($"Missing surface maps for {surface.TextureFamily}.");

            var tiling = new Vector2(surface.Tiling, surface.Tiling);
            if (material.GetTexture("_BaseMap") == albedo && material.GetTexture("_BumpMap") == normal
                && material.GetTextureScale("_BaseMap") == tiling && material.IsKeywordEnabled("_NORMALMAP")
                && Mathf.Approximately(material.GetFloat("_BumpScale"), surface.Relief)) return false;

            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", surface.Relief);
            material.EnableKeyword("_NORMALMAP");
            return true;
        }
    }
}
