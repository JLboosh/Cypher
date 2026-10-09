using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Menu: Cypher > Build Workshop Scene.
    /// Generates the whole Phase 1 scene (desk, lab, lights, post-processing, camera rig,
    /// hologram prefab, trash can, managers) and wires every Inspector reference.
    /// Materials and the post-processing profile are only created if missing, so your
    /// tweaks to them survive a rebuild. The scene and the hologram prefab are regenerated.
    /// </summary>
    public static class WorkshopSceneBuilder
    {
        const string Root = "Assets/Cypher";
        const string GenDir = Root + "/Generated";
        const string MatDir = GenDir + "/Materials";
        const string ScenePath = Root + "/Scenes/Workshop.unity";
        const string PrefabPath = GenDir + "/Hologram.prefab";
        const string ProfilePath = GenDir + "/WorkshopPostProcessing.asset";

        const float PanelWidth = 0.42f;
        const float PanelHeight = 0.26f;
        const float DeskHeight = 0.75f;

        static readonly Color FogColor = new Color(0.07f, 0.075f, 0.085f); // workshop haze

        struct Mats
        {
            public Material hologram, holoSolid, holoText, deskTop, deskBody, glowStrip, floor, emissiveCyan, mote, dust, uiRect, uiFlat, voiceRing, hand;
        }

        [MenuItem("Cypher/Build Workshop Scene", priority = 0)]
        static void BuildFromMenu() => Build(askFirst: true);

        public static void Build(bool askFirst)
        {
            if (!PlayModeGuard.EnsureEditMode("scene", "Building the workshop scene")) return;
            if (askFirst && !EditorUtility.DisplayDialog("Build Workshop Scene",
                    $"This creates (or regenerates) {ScenePath} and the hologram prefab.\n\n" +
                    "Your saved tasks are not touched.", "Build", "Cancel"))
                return;

            var font = LoadDefaultFont();
            if (font == null) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Root, "Scenes");
            EnsureFolder(Root, "Generated");
            EnsureFolder(GenDir, "Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var mats = CreateMaterials(font);
            var profile = GetOrCreatePostProfile();
            var hologramPrefab = BuildHologramPrefab(mats, font);

            ConfigureRenderSettings();
            BuildLights();
            BuildEnvironment(mats);
            var (rig, cam) = BuildCamera();
            BuildVolume(profile);
            BuildTrashCan(mats);
            BuildSystems(hologramPrefab, rig, cam);
            BuildEditPanel(mats, font);
            SetRef(new GameObject("Cypher Voice").AddComponent<VoiceAssistant>(), "ringMaterial", mats.voiceRing);

            var removal = new GameObject("Cypher Removal").AddComponent<CrumpleThrowDirector>();
            SetRef(removal, "handMaterial", mats.hand);
            SetRef(removal, "lineMaterial", mats.voiceRing);
            SetRef(removal, "sparkMaterial", mats.mote);
            SetRef(removal, "crumpleShader", Shader.Find("Cypher/HologramCrumple"));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"[Cypher] Workshop scene built at {ScenePath}. Press Play!");
        }

        [MenuItem("Cypher/Open Save Folder", priority = 20)]
        static void OpenSaveFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath + "/");

        [MenuItem("Cypher/Open Config File (API keys, voice)", priority = 22)]
        static void OpenConfig()
        {
            CypherConfig.Load(); // creates it with defaults if missing
            EditorUtility.OpenWithDefaultApp(CypherConfig.FilePath);
        }

        [MenuItem("Cypher/Delete Saved Tasks", priority = 21)]
        static void DeleteSave()
        {
            string path = new TaskRepository().FilePath;
            if (!System.IO.File.Exists(path))
            {
                EditorUtility.DisplayDialog("Cypher", "There is no save file yet.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Delete Saved Tasks", $"Delete {path}?\nA .bak copy is kept next to it.", "Delete", "Cancel"))
                return;
            System.IO.File.Copy(path, path + ".bak", true);
            System.IO.File.Delete(path);
        }

        // ---------------------------------------------------------------- checks & folders

        static TMP_FontAsset LoadDefaultFont()
        {
            // Loaded directly (not via TMP_Settings.instance) so a missing import gives a clear message.
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings == null || TMP_Settings.defaultFontAsset == null)
            {
                EditorUtility.DisplayDialog("TextMeshPro not set up",
                    "Import the TextMeshPro essentials first:\n\n" +
                    "Window > TextMeshPro > Import TMP Essential Resources\n\n" +
                    "then run Cypher > Build Workshop Scene again.", "OK");
                return null;
            }
            return TMP_Settings.defaultFontAsset;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
                AssetDatabase.CreateFolder(parent, name);
        }

        // ---------------------------------------------------------------- materials & post

        static Mats CreateMaterials(TMP_FontAsset font)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var holo = Shader.Find("Cypher/Hologram");
            var glowParticle = Shader.Find("Cypher/GlowParticle");
            var uiGlow = Shader.Find("Cypher/UIGlow");
            var glowLine = Shader.Find("Cypher/GlowLine");
            if (holo == null) Debug.LogError("[Cypher] Shader 'Cypher/Hologram' not found. Check the Console for shader errors.");

            return new Mats
            {
                hologram = GetOrCreateMaterial("Hologram", holo, m => m.SetVector("_PanelSize", new Vector4(PanelWidth, PanelHeight))),
                holoSolid = GetOrCreateMaterial("HoloSolid", unlit, m => m.SetColor("_BaseColor", new Color(0.5f, 1.8f, 2.6f))),
                holoText = GetOrCreateMaterial("HoloText", font.material.shader, m =>
                {
                    m.CopyPropertiesFromMaterial(font.material);
                    m.SetColor("_FaceColor", new Color(0.8f, 1.15f, 1.35f, 1f));
                    // Soft dark halo behind letters so they read against the glowing panel.
                    m.EnableKeyword("UNDERLAY_ON");
                    m.SetColor("_UnderlayColor", new Color(0f, 0.015f, 0.04f, 0.85f));
                    m.SetFloat("_UnderlayDilate", 0.35f);
                    m.SetFloat("_UnderlaySoftness", 0.45f);
                }),
                deskTop = GetOrCreateMaterial("DeskTop", lit, m => SetupLit(m, new Color(0.045f, 0.055f, 0.07f), 0.75f, 0.82f)),
                deskBody = GetOrCreateMaterial("DeskBody", lit, m => SetupLit(m, new Color(0.02f, 0.025f, 0.035f), 0.6f, 0.55f)),
                glowStrip = GetOrCreateMaterial("GlowStrip", lit, m => SetupLit(m, Color.black, 0f, 0.5f, new Color(0.3f, 1.6f, 2.6f))),
                floor = GetOrCreateMaterial("LabFloor", lit, m => SetupLit(m, new Color(0.018f, 0.022f, 0.03f), 0.5f, 0.78f)),
                emissiveCyan = GetOrCreateMaterial("LabEmissive", lit, m => SetupLit(m, Color.black, 0f, 0.3f, new Color(0.1f, 0.6f, 1.1f))),
                mote = GetOrCreateMaterial("HologramMote", glowParticle, m => m.SetColor("_Tint", new Color(0.6f, 1.8f, 2.8f, 1f))),
                dust = GetOrCreateMaterial("LabDust", glowParticle, m => m.SetColor("_Tint", new Color(0.25f, 0.55f, 0.8f, 1f))),
                uiRect = GetOrCreateMaterial("UIGlowFrame", uiGlow, m => { }),
                voiceRing = GetOrCreateMaterial("VoiceRing", glowLine, m => { }),
                hand = GetOrCreateMaterial("HoloHand", Shader.Find("Cypher/HoloHand"), m => { }),
                uiFlat = GetOrCreateMaterial("UIGlowFlat", uiGlow, m =>
                {
                    m.SetFloat("_BorderPx", 0f);
                    m.SetFloat("_Intensity", 2.4f);
                }),
            };
        }

        static void SetupLit(Material m, Color baseColor, float metallic, float smoothness, Color? emission = null)
        {
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
        }

        static Material GetOrCreateMaterial(string name, Shader shader, System.Action<Material> init)
        {
            string path = $"{MatDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(shader) { name = name };
            init(mat);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static VolumeProfile GetOrCreatePostProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.7f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0f, 0.02f, 0.04f));

            profile.Add<ChromaticAberration>(true).intensity.Override(0.04f); // higher fringes small text
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.contrast.Override(12f);

            // Each effect is a sub-asset; without this they'd vanish on reload.
            foreach (var component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ---------------------------------------------------------------- hologram prefab

        static HologramPanel BuildHologramPrefab(Mats mats, TMP_FontAsset font)
        {
            var root = new GameObject("Hologram");
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(PanelWidth, PanelHeight, 0.02f);
            var panel = root.AddComponent<HologramPanel>();

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var quad = Primitive(PrimitiveType.Quad, "Panel", visual, mats.hologram);
            quad.transform.localScale = new Vector3(PanelWidth, PanelHeight, 1f);

            var divider = Primitive(PrimitiveType.Quad, "Divider", visual, mats.holoSolid);
            divider.transform.localPosition = new Vector3(0f, -0.058f, -0.002f);
            divider.transform.localScale = new Vector3(0.36f, 0.0012f, 1f);

            var title = Text("Title", visual, font, mats.holoText, new Vector3(0f, 0.018f, -0.003f),
                new Vector2(0.36f, 0.12f), TextAlignmentOptions.Center, 0.1f, 0.45f, "Task title");
            title.fontStyle = FontStyles.Bold;
            var due = Text("Due", visual, font, mats.holoText, new Vector3(-0.08f, -0.088f, -0.003f),
                new Vector2(0.2f, 0.035f), TextAlignmentOptions.Left, 0.06f, 0.22f, "DUE TOMORROW");
            var priority = Text("Priority", visual, font, mats.holoText, new Vector3(0.09f, -0.088f, -0.003f),
                new Vector2(0.16f, 0.035f), TextAlignmentOptions.Right, 0.06f, 0.22f, "PRI · AUTO");
            var source = Text("Source", visual, font, mats.holoText, new Vector3(-0.14f, 0.103f, -0.003f),
                new Vector2(0.1f, 0.028f), TextAlignmentOptions.Left, 0.05f, 0.16f, "");

            BuildPencil(visual, mats.holoSolid);
            var motes = BuildMotes(root.transform, mats.mote);

            SetRef(panel, "visual", visual);
            SetRef(panel, "panelRenderer", quad.GetComponent<Renderer>());
            SetRef(panel, "titleText", title);
            SetRef(panel, "dueText", due);
            SetRef(panel, "priorityText", priority);
            SetRef(panel, "sourceText", source);
            SetRef(panel, "motes", motes);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<HologramPanel>();
        }

        static void BuildPencil(Transform parent, Material mat)
        {
            var pencil = new GameObject("Pencil");
            pencil.transform.SetParent(parent, false);
            pencil.transform.localPosition = new Vector3(PanelWidth / 2f - 0.028f, PanelHeight / 2f - 0.028f, -0.004f);
            pencil.AddComponent<HologramPencil>();

            // Sits in front of the panel's own collider so the raycast hits the pencil first.
            var hitBox = pencil.AddComponent<BoxCollider>();
            hitBox.size = new Vector3(0.042f, 0.042f, 0.01f);
            hitBox.center = new Vector3(0f, 0f, -0.012f);

            var icon = new GameObject("Icon").transform;
            icon.SetParent(pencil.transform, false);
            icon.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var body = Primitive(PrimitiveType.Cube, "Body", icon, mat);
            body.transform.localScale = new Vector3(0.026f, 0.0065f, 0.002f);

            var tip = Primitive(PrimitiveType.Cube, "Tip", icon, mat);
            tip.transform.localPosition = new Vector3(-0.0165f, 0f, 0f);
            tip.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            tip.transform.localScale = new Vector3(0.0046f, 0.0046f, 0.002f);
        }

        /// <summary>A few glowing specks drifting up around the panel. Also used for build/un-build sparks.</summary>
        static ParticleSystem BuildMotes(Transform parent, Material mat)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.002f, 0.01f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.007f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.95f, 1f, 0.9f), new Color(0.3f, 0.7f, 1f, 0.5f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // motes travel with the panel when dragged
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 60;

            var emission = ps.emission;
            emission.rateOverTime = 5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(PanelWidth, PanelHeight, 0.04f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.003f, 0.003f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.006f, 0.018f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.003f, 0.003f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.01f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.2f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = FadeInOut();

            ConfigureParticleRenderer(go, mat, 3);
            return ps;
        }

        static Gradient FadeInOut()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static void ConfigureParticleRenderer(GameObject go, Material mat, int sortingOrder)
        {
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingOrder = sortingOrder;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static TextMeshPro Text(string name, Transform parent, TMP_FontAsset font, Material mat, Vector3 localPos,
            Vector2 size, TextAlignmentOptions align, float minSize, float maxSize, string sample)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.fontSharedMaterial = mat;
            t.text = sample;
            t.alignment = align;
            t.enableAutoSizing = true;
            t.fontSizeMin = minSize;
            t.fontSizeMax = maxSize;
            t.overflowMode = TextOverflowModes.Truncate; // long titles are shortened in HologramPanel
            t.rectTransform.sizeDelta = size;
            t.sortingOrder = 2; // draw after the translucent panel
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        // ---------------------------------------------------------------- scene

        static void ConfigureRenderSettings()
        {
            RenderSettings.skybox = null;
            // Indoor bounce light: cool from above, neutral at eye level, dark from the floor.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.2f, 0.22f, 0.26f);
            RenderSettings.ambientEquatorColor = new Color(0.13f, 0.13f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.028f;
            RenderSettings.fogColor = FogColor;
        }

        static void BuildLights()
        {
            var parent = new GameObject("Lighting").transform;

            var moon = NewLight("Fill (Directional)", parent, LightType.Directional, new Color(0.7f, 0.78f, 0.9f), 0.35f);
            moon.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            moon.shadows = LightShadows.None;

            var key = NewLight("Desk Key (Spot)", parent, LightType.Spot, new Color(0.75f, 0.9f, 1f), 4f);
            key.transform.SetPositionAndRotation(new Vector3(0f, 2.8f, 0.55f), Quaternion.Euler(90f, 0f, 0f));
            key.range = 5f;
            key.spotAngle = 85f;
            key.innerSpotAngle = 40f;
            key.shadows = LightShadows.Soft;

            var holoGlow = NewLight("Hologram Glow (Point)", parent, LightType.Point, new Color(0.3f, 0.8f, 1f), 1.2f);
            holoGlow.transform.position = new Vector3(0f, 1.25f, 0.95f);
            holoGlow.range = 2.2f;
            holoGlow.shadows = LightShadows.None;

            var rim = NewLight("Holo Accent (Point)", parent, LightType.Point, new Color(0.15f, 0.55f, 1f), 1.2f);
            rim.transform.position = new Vector3(0f, 2.2f, 3.2f);
            rim.range = 5f;
            rim.shadows = LightShadows.None;
        }

        static Light NewLight(string name, Transform parent, LightType type, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            return light;
        }

        static void BuildEnvironment(Mats mats)
        {
            var env = new GameObject("Environment").transform;

            // The room: concrete floor, block + corrugated walls, steel beams, benches, shelves,
            // machines and lamps (CC0 models from Poly Haven). See WorkshopEnvironment.cs.
            WorkshopEnvironment.Build(env);

            var desk = new GameObject("Desk").transform;
            desk.SetParent(env, false);
            Curved("Desk Top", desk, mats.deskTop, DeskHeight, 0.55f, 1.15f, 160f, 0.04f, 96);
            Curved("Desk Pedestal", desk, mats.deskBody, DeskHeight - 0.04f, 0.82f, 1.02f, 140f, DeskHeight - 0.04f, 64);
            Curved("Front Glow Strip", desk, mats.glowStrip, DeskHeight + 0.002f, 0.54f, 0.552f, 160f, 0.046f, 96);
            Curved("Back Glow Strip", desk, mats.glowStrip, DeskHeight + 0.002f, 1.148f, 1.156f, 160f, 0.01f, 96);

            Curved("Floor Light Ring", env, mats.emissiveCyan, 0.006f, 2.6f, 2.64f, 360f, 0.005f, 160);

            BuildAmbientDust(env, mats.dust);
        }

        /// <summary>Faint specks floating in the lab air, catching the hologram light.</summary>
        static void BuildAmbientDust(Transform parent, Material mat)
        {
            var go = new GameObject("Ambient Dust");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 1f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.8f, 1f, 0.35f), new Color(0.3f, 0.6f, 1f, 0.15f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 250;

            var emission = ps.emission;
            emission.rateOverTime = 22f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7f, 3f, 7f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.04f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.05f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = FadeInOut();

            ConfigureParticleRenderer(go, mat, 0);
        }

        static void Curved(string name, Transform parent, Material mat, float y, float inner, float outer,
            float arc, float thickness, int segments)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var mesh = go.AddComponent<CurvedDeskMesh>();
            mesh.innerRadius = inner;
            mesh.outerRadius = outer;
            mesh.arcDegrees = arc;
            mesh.thickness = thickness;
            mesh.segments = segments;
            mesh.Rebuild();
        }

        static (CameraRigController rig, Camera cam) BuildCamera()
        {
            var rigGo = new GameObject("CameraRig");
            var rig = rigGo.AddComponent<CameraRigController>();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(rigGo.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            camGo.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 60f;
            camGo.AddComponent<AudioListener>();

            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            return (rig, cam);
        }

        static void BuildVolume(VolumeProfile profile)
        {
            var go = new GameObject("Post Processing (Global Volume)");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        /// <summary>
        /// A life-size holographic bin in the front-right corner of the workshop (visible from
        /// the seat, about 39 degrees to the right). Removed tasks are thrown into it.
        /// </summary>
        static void BuildTrashCan(Mats mats)
        {
            const float radius = 0.24f, height = 0.62f;
            var root = new GameObject("Trash Can");
            root.transform.position = new Vector3(6.2f, 0f, 7.85f);

            var body = Primitive(PrimitiveType.Cylinder, "Body", root.transform, mats.hologram);
            body.transform.localPosition = new Vector3(0f, height / 2f, 0f);
            body.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);

            var rim = new GameObject("Rim");
            rim.transform.SetParent(root.transform, false);
            Curved("Rim Ring", rim.transform, mats.glowStrip, height + 0.005f, radius - 0.012f, radius + 0.012f, 360f, 0.012f, 64);
            Curved("Floor Ring", rim.transform, mats.emissiveCyan, 0.006f, radius + 0.08f, radius + 0.11f, 360f, 0.005f, 64);

            var mouth = new GameObject("Mouth").transform;
            mouth.SetParent(root.transform, false);
            mouth.localPosition = new Vector3(0f, height - 0.02f, 0f);

            var glow = NewLight("Trash Glow", root.transform, LightType.Point, new Color(0.3f, 0.8f, 1f), 1.4f);
            glow.transform.localPosition = new Vector3(0f, height + 0.15f, 0f);
            glow.range = 2.2f;
            glow.shadows = LightShadows.None;

            var trash = root.AddComponent<TrashCan>();
            SetRef(trash, "glowRenderer", body.GetComponent<Renderer>());
            SetRef(trash, "mouth", mouth);
            var so = new SerializedObject(trash);
            so.FindProperty("mouthRadius").floatValue = radius - 0.02f;
            so.FindProperty("rippleMaxRadius").floatValue = radius * 2.6f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildSystems(HologramPanel prefab, CameraRigController rig, Camera cam)
        {
            var holograms = new GameObject("Holograms").transform;

            var systems = new GameObject("Systems");
            var manager = systems.AddComponent<TaskManager>();
            SetRef(manager, "hologramPrefab", prefab);
            SetRef(manager, "hologramRoot", holograms);

            var interaction = systems.AddComponent<InteractionController>();
            SetRef(interaction, "cam", cam);
            SetRef(interaction, "cameraRig", rig);

            systems.AddComponent<CypherAudio>();

            var hotkeys = systems.AddComponent<DebugTaskHotkeys>();
            SetRef(hotkeys, "interaction", interaction);
        }

        static void BuildEditPanel(Mats mats, TMP_FontAsset font)
        {
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var editPanel = new GameObject("Edit Panel").AddComponent<EditPanelController>();
            SetRef(editPanel, "font", font);
            SetRef(editPanel, "uiRectMaterial", mats.uiRect);
            SetRef(editPanel, "uiFlatMaterial", mats.uiFlat);
            SetRef(editPanel, "hologramMaterial", mats.hologram);
            SetRef(editPanel, "uiGlassMaterial", GetOrCreateMaterial("UIGlass", Shader.Find("Cypher/UIGlass"), m => { }));
        }

        // ---------------------------------------------------------------- helpers

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // only holograms are clickable
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            if (mat != null && mat.shader.name == "Cypher/Hologram")
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[Cypher] {target.GetType().Name} has no serialized field '{field}'.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
