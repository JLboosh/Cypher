using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Turns the downloaded Poly Haven (CC0) assets into URP-ready pieces:
    ///   - texture import settings (normal maps flagged, data maps linear, max 1024 px),
    ///   - one URP Lit material per texture set, with a generated metallic/smoothness map
    ///     (URP wants metal in R and smoothness = 1 - roughness in A),
    ///   - glass sets become transparent, emissive sets glow warm.
    /// Materials are cached as assets in ThirdParty/PolyHaven/Materials.
    /// </summary>
    public static class PolyHavenLibrary
    {
        public const string Root = "Assets/Cypher/ThirdParty/PolyHaven";
        const string MaterialDir = Root + "/Materials";

        static readonly Regex MapSuffix = new Regex(
            @"_(diff|col|color|nor_gl|nor_dx|rough|roughness|metal|metallic|arm|ao|emissive|emission|alpha|opacity|mask\d*|ms)(_1k)?\.(jpg|png)$",
            RegexOptions.IgnoreCase);

        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        // ------------------------------------------------------------------ import settings

        /// <summary>Applied automatically on import, and re-checked before building the scene.</summary>
        public static bool ConfigureTexture(TextureImporter ti, string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool changed = false;
            if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; changed = true; }
            if (name.Contains("_nor_gl"))
            {
                if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
            }
            else if (Regex.IsMatch(name, @"_(rough|roughness|metal|metallic|arm|ao|ms|mask\d*)(_1k)?$"))
            {
                if (ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
            }
            return changed;
        }

        public static void PrepareTextures()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Root });
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(path) is TextureImporter ti && ConfigureTexture(ti, path))
                        ti.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        // ------------------------------------------------------------------ materials

        /// <summary>The model prefab (FBX) for a Poly Haven model id, or null if not downloaded.</summary>
        public static GameObject Model(string id) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{id}/{id}.fbx");

        /// <summary>Texture-set names in a model folder ("metal_tool_chest", "..._glass").</summary>
        public static List<string> SetsFor(string modelId)
        {
            string dir = $"{Root}/Models/{modelId}/textures";
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir)
                .Where(f => MapSuffix.IsMatch(Path.GetFileName(f)))
                .Select(f => MapSuffix.Replace(Path.GetFileName(f), ""))
                .Distinct().OrderByDescending(s => s.Length).ToList();
        }

        /// <summary>URP Lit material for a texture set in a model folder.</summary>
        public static Material ModelMaterial(string modelId, string set) =>
            GetOrCreate($"{modelId}__{set}", $"{Root}/Models/{modelId}/textures", set);

        /// <summary>URP Lit material for a tiling surface texture (floor, walls), with tiling.</summary>
        public static Material SurfaceMaterial(string textureId, Vector2 tiling, string variant = "")
        {
            var m = GetOrCreate(textureId + variant, $"{Root}/Textures/{textureId}", textureId);
            if (m != null && m.GetTextureScale("_BaseMap") != tiling)
            {
                foreach (var prop in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" })
                    if (m.HasProperty(prop)) m.SetTextureScale(prop, tiling);
                EditorUtility.SetDirty(m);
            }
            return m;
        }

        /// <summary>Replaces each renderer's imported materials with our URP materials, matched by name.</summary>
        public static void ApplyMaterials(GameObject instance, string modelId)
        {
            var sets = SetsFor(modelId);
            if (sets.Count == 0) return;
            string main = sets.FirstOrDefault(s => s.Equals(modelId, System.StringComparison.OrdinalIgnoreCase)) ?? sets.Last();

            foreach (var r in instance.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                    string set = sets.FirstOrDefault(s => n == s.ToLowerInvariant())
                                 ?? sets.FirstOrDefault(s => n.Contains(s.ToLowerInvariant()) || s.ToLowerInvariant().Contains(n) && n.Length > 2)
                                 ?? main;
                    mats[i] = ModelMaterial(modelId, set);
                }
                r.sharedMaterials = mats;
            }
        }

        static Material GetOrCreate(string key, string folder, string set)
        {
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!AssetDatabase.IsValidFolder(MaterialDir))
                AssetDatabase.CreateFolder(Root, "Materials");

            string path = $"{MaterialDir}/{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = Build(folder, set);
                if (mat == null) return null;
                AssetDatabase.CreateAsset(mat, path);
            }
            cache[key] = mat;
            return mat;
        }

        static Material Build(string folder, string set)
        {
            if (!Directory.Exists(folder)) return null;
            string Find(params string[] maps)
            {
                foreach (var m in maps)
                    foreach (var ext in new[] { "jpg", "png" })
                    {
                        string p = $"{folder}/{set}_{m}_1k.{ext}";
                        if (File.Exists(p)) return p;
                    }
                return null;
            }

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = set };
            string diff = Find("diff", "col", "color");
            string normal = Find("nor_gl");
            string arm = Find("arm");
            string rough = Find("rough", "roughness");
            string metal = Find("metal", "metallic");
            string emissive = Find("emissive", "emission");

            if (diff != null) mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(diff));
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                mat.EnableKeyword("_NORMALMAP");
            }

            var ms = BuildMetallicSmoothness(folder, set, arm, rough, metal);
            if (ms != null)
            {
                mat.SetTexture("_MetallicGlossMap", ms);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", 1f);
            }
            else
            {
                mat.SetFloat("_Smoothness", 0.35f);
            }

            if (emissive != null)
            {
                mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emissive));
                mat.SetColor("_EmissionColor", new Color(2.6f, 2.0f, 1.35f)); // warm bulb glow
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            if (set.ToLowerInvariant().Contains("glass")) MakeGlass(mat);
            return mat;
        }

        /// <summary>URP wants metallic in R and smoothness (1 - roughness) in A, in one texture.</summary>
        static Texture2D BuildMetallicSmoothness(string folder, string set, string arm, string rough, string metal)
        {
            if (arm == null && rough == null) return null;
            string outPath = $"{folder}/{set}_ms_1k.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (existing != null) return existing;

            Color32[] Load(string p, out int w, out int h)
            {
                var t = new Texture2D(2, 2);
                t.LoadImage(File.ReadAllBytes(p));
                w = t.width;
                h = t.height;
                var px = t.GetPixels32();
                Object.DestroyImmediate(t);
                return px;
            }

            Color32[] armPx = null, roughPx = null, metalPx = null;
            int width = 0, height = 0, w2, h2;
            if (arm != null) armPx = Load(arm, out width, out height);
            else roughPx = Load(rough, out width, out height);
            if (arm == null && metal != null)
            {
                metalPx = Load(metal, out w2, out h2);
                if (w2 != width || h2 != height) metalPx = null;
            }

            var result = new Color32[width * height];
            for (int i = 0; i < result.Length; i++)
            {
                byte r = armPx != null ? armPx[i].g : roughPx[i].r;   // roughness
                byte m = armPx != null ? armPx[i].b : metalPx != null ? metalPx[i].r : (byte)0;
                result[i] = new Color32(m, 0, 0, (byte)(255 - r));
            }
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(result);
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(outPath);
            if (AssetImporter.GetAtPath(outPath) is TextureImporter ti)
            {
                ti.sRGBTexture = false;
                ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        static void MakeGlass(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.85f, 0.35f));
            mat.SetFloat("_Smoothness", 0.95f);
        }
    }

    /// <summary>Import rules for everything under ThirdParty/PolyHaven.</summary>
    public class PolyHavenImportRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(PolyHavenLibrary.Root)) return;
            PolyHavenLibrary.ConfigureTexture((TextureImporter)assetImporter, assetPath);
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(PolyHavenLibrary.Root)) return;
            var mi = (ModelImporter)assetImporter;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.animationType = ModelImporterAnimationType.None;
        }
    }
}
