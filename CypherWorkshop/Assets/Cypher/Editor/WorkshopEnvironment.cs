using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Builds the physical workshop around the holographic desk: a concrete-and-corrugated-steel
    /// room with steel ceiling beams, a workbench wall (vise, drill, tools, desk lamp, multimeter,
    /// circuit board), a storage wall (steel shelving, crates, tool chest), machines (drill press,
    /// welding cart), a roller-shutter door, pipes and cables, and warm hanging lamps.
    /// All props are CC0 models from Poly Haven (see ThirdParty/PolyHaven/CREDITS.md).
    ///
    /// The seat is at the origin facing +Z. Props are placed by their measured bounds
    /// (bottom-center on a floor/bench point, or flush against a wall), so model pivots don't matter.
    /// Missing models are skipped with a warning.
    /// </summary>
    public static class WorkshopEnvironment
    {
        // Room extents (meters).
        const float MinX = -7f, MaxX = 7f, MinZ = -6f, MaxZ = 8.5f, Height = 4.6f, Wall = 0.3f;
        const float BenchX = MinX + 0.55f;   // left workbench line
        const float ShelfX = MaxX - 0.45f;   // right storage line

        static Transform root;

        public static void Build(Transform parent)
        {
            PolyHavenLibrary.PrepareTextures();
            root = new GameObject("Workshop").transform;
            root.SetParent(parent, false);

            BuildShell();
            BuildWorkbenchWall();
            BuildStorageWall();
            BuildFrontWall();
            BuildBackWall();
            BuildCeilingLamps();
            BuildReflectionProbe();

            foreach (var t in root.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        // ------------------------------------------------------------------ room

        static void BuildShell()
        {
            var shell = Group("Shell");
            float w = MaxX - MinX, d = MaxZ - MinZ, cx = (MinX + MaxX) / 2f, cz = (MinZ + MaxZ) / 2f;

            var floorMat = PolyHavenLibrary.SurfaceMaterial("garage_floor", new Vector2(w / 2.5f, d / 2.5f));
            Box("Floor", shell, new Vector3(cx, -0.05f, cz), new Vector3(w, 0.1f, d), floorMat, castShadows: false);

            // Walls: concrete block up to 2.6 m, corrugated steel above.
            var block = PolyHavenLibrary.SurfaceMaterial("concrete_block_wall", new Vector2(w / 2f, 2.6f / 2f));
            var blockSide = PolyHavenLibrary.SurfaceMaterial("concrete_block_wall", new Vector2(d / 2f, 2.6f / 2f), "_side");
            var iron = PolyHavenLibrary.SurfaceMaterial("corrugated_iron_02", new Vector2(w / 2f, 1f));
            var ironSide = PolyHavenLibrary.SurfaceMaterial("corrugated_iron_02", new Vector2(d / 2f, 1f), "_side");
            float lower = 2.6f, upper = Height - lower;

            Box("Wall Front Lower", shell, new Vector3(cx, lower / 2f, MaxZ + Wall / 2f), new Vector3(w + Wall * 2, lower, Wall), block);
            Box("Wall Front Upper", shell, new Vector3(cx, lower + upper / 2f, MaxZ + Wall / 2f), new Vector3(w + Wall * 2, upper, Wall), iron);
            Box("Wall Back Lower", shell, new Vector3(cx, lower / 2f, MinZ - Wall / 2f), new Vector3(w + Wall * 2, lower, Wall), block);
            Box("Wall Back Upper", shell, new Vector3(cx, lower + upper / 2f, MinZ - Wall / 2f), new Vector3(w + Wall * 2, upper, Wall), iron);
            Box("Wall Left Lower", shell, new Vector3(MinX - Wall / 2f, lower / 2f, cz), new Vector3(Wall, lower, d), blockSide);
            Box("Wall Left Upper", shell, new Vector3(MinX - Wall / 2f, lower + upper / 2f, cz), new Vector3(Wall, upper, d), ironSide);
            Box("Wall Right Lower", shell, new Vector3(MaxX + Wall / 2f, lower / 2f, cz), new Vector3(Wall, lower, d), blockSide);
            Box("Wall Right Upper", shell, new Vector3(MaxX + Wall / 2f, lower + upper / 2f, cz), new Vector3(Wall, upper, d), ironSide);

            // Ceiling + painted steel I-beams and columns.
            var steel = PolyHavenLibrary.SurfaceMaterial("blue_metal_plate", new Vector2(6f, 0.3f));
            var ceiling = PolyHavenLibrary.SurfaceMaterial("corrugated_iron_02", new Vector2(w / 2f, d / 2f), "_ceiling");
            Box("Ceiling", shell, new Vector3(cx, Height + 0.05f, cz), new Vector3(w, 0.1f, d), ceiling, castShadows: false);
            for (float z = MinZ + 1.5f; z < MaxZ; z += 3f)
            {
                Box("Beam", shell, new Vector3(cx, Height - 0.2f, z), new Vector3(w, 0.32f, 0.14f), steel);
                Box("Beam Flange", shell, new Vector3(cx, Height - 0.37f, z), new Vector3(w, 0.03f, 0.3f), steel);
            }
            foreach (float x in new[] { MinX + 0.15f, MaxX - 0.15f })
                for (float z = MinZ + 1.5f; z < MaxZ; z += 6f)
                    Box("Column", shell, new Vector3(x, Height / 2f, z), new Vector3(0.28f, Height, 0.28f), steel);

            // Yellow safety line around the work area.
            var paint = Lit("Safety Paint", new Color(0.85f, 0.65f, 0.08f), 0f, 0.35f);
            Box("Safety Line L", shell, new Vector3(-2.6f, 0.002f, 1.2f), new Vector3(0.08f, 0.004f, 6.5f), paint, castShadows: false);
            Box("Safety Line R", shell, new Vector3(2.6f, 0.002f, 1.2f), new Vector3(0.08f, 0.004f, 6.5f), paint, castShadows: false);
        }

        // ------------------------------------------------------------------ zones

        static void BuildWorkbenchWall()
        {
            var zone = Group("Workbench Wall (left)");

            // Two wooden tables end to end along the wall = one long bench.
            var benchA = Prop("WoodenTable_03", zone, new Vector3(BenchX, 0f, 0.6f), 90f, 0.8f);
            var benchB = Prop("WoodenTable_03", zone, new Vector3(BenchX, 0f, 2.6f), 90f, 0.8f);
            SnapToWall(benchA, Vector3.left, MinX);
            SnapToWall(benchB, Vector3.left, MinX);
            float top = Top(benchA, 0.85f);

            Prop("bench_vice_01", zone, new Vector3(BenchX + 0.05f, top, -0.1f), 90f, 0.25f);
            Prop("Drill_01", zone, new Vector3(BenchX + 0.1f, top, 0.45f), 30f, 0.25f);
            Prop("adjustable_wrench", zone, new Vector3(BenchX + 0.25f, top, 0.8f), 75f, 0.04f);
            Prop("pliers", zone, new Vector3(BenchX + 0.3f, top, 1.05f), 110f, 0.04f);
            Prop("screwdrivers_02", zone, new Vector3(BenchX + 0.15f, top, 1.3f), 90f, 0.06f);
            Prop("measuring_tape_01", zone, new Vector3(BenchX + 0.3f, top, 1.55f), 40f, 0.06f);
            Prop("circuit_board", zone, new Vector3(BenchX + 0.1f, top, 1.95f), 95f, 0.04f);
            Prop("retro_multimeter", zone, new Vector3(BenchX + 0.05f, top, 2.4f), 70f, 0.2f);
            Prop("desk_lamp_arm_01", zone, new Vector3(BenchX - 0.05f, top, 2.85f), 120f, 0.5f);
            Prop("cross_pein_hammer", zone, new Vector3(BenchX + 0.3f, top, 3.15f), 80f, 0.04f);
            Prop("small_oil_can_01", zone, new Vector3(BenchX + 0.0f, top, 3.45f), 0f, 0.2f);

            Prop("metal_stool_01", zone, new Vector3(BenchX + 1.0f, 0f, 1.5f), -70f, 0.65f);
            Prop("metal_toolbox", zone, new Vector3(BenchX + 0.2f, 0f, 4.3f), 90f, 0.3f);

            // Wall lights over the bench, and the power box.
            var lampA = Prop("industrial_wall_lamp", zone, new Vector3(MinX + 0.1f, 2.15f, 0.6f), 90f, 0.35f);
            var lampB = Prop("industrial_wall_lamp", zone, new Vector3(MinX + 0.1f, 2.15f, 2.6f), 90f, 0.35f);
            SnapToWall(lampA, Vector3.left, MinX);
            SnapToWall(lampB, Vector3.left, MinX);
            var power = Prop("power_box_01", zone, new Vector3(MinX + 0.1f, 1.3f, 4.6f), 90f, 0.6f);
            SnapToWall(power, Vector3.left, MinX);

            WarmLight("Bench Wall Light", zone, new Vector3(MinX + 0.6f, 2.0f, 1.6f), LightType.Point, 2.2f, 4.5f, false);

            var pipes = Prop("modular_industrial_pipes_01", zone, new Vector3(MinX + 0.2f, 3.2f, 2f), 90f, null);
            if (pipes != null) SnapToWall(pipes, Vector3.left, MinX);
        }

        static void BuildStorageWall()
        {
            var zone = Group("Storage Wall (right)");

            var shelfA = Prop("steel_frame_shelves_01", zone, new Vector3(ShelfX, 0f, 1.6f), -90f, 1.8f);
            var shelfB = Prop("steel_frame_shelves_02", zone, new Vector3(ShelfX, 0f, 3.6f), -90f, 1.8f);
            SnapToWall(shelfA, Vector3.right, MaxX);
            SnapToWall(shelfB, Vector3.right, MaxX);
            float topA = Top(shelfA, 1.8f), topB = Top(shelfB, 1.8f);

            Prop("plastic_crate_03", zone, new Vector3(ShelfX - 0.1f, topA, 1.6f), -90f, 0.3f);
            Prop("cardboard_box_01", zone, new Vector3(ShelfX - 0.1f, topB, 3.4f), -80f, 0.35f);
            Prop("spray_paint_bottles", zone, new Vector3(ShelfX - 0.1f, topB, 3.95f), -90f, 0.2f);

            Prop("metal_tool_chest", zone, new Vector3(MaxX - 0.45f, 0f, -0.6f), -90f, 1.1f);
            Prop("old_military_crate", zone, new Vector3(MaxX - 0.6f, 0f, 5.4f), -100f, 0.45f);
            Prop("cardboard_box_01", zone, new Vector3(MaxX - 1.3f, 0f, 5.6f), -60f, 0.35f);
            Prop("plastic_crate_03", zone, new Vector3(MaxX - 0.6f, 0.0f, 6.6f), -85f, 0.3f);

            var cables = Prop("modular_electric_cables", zone, new Vector3(MaxX - 0.2f, 3.0f, 2.5f), -90f, null);
            if (cables != null) SnapToWall(cables, Vector3.right, MaxX);

            WarmLight("Storage Fill", zone, new Vector3(MaxX - 1.2f, 2.6f, 2.6f), LightType.Point, 1.6f, 5f, false);
        }

        static void BuildFrontWall()
        {
            var zone = Group("Front Wall");

            var door = Prop("rollershutter_door", zone, new Vector3(1.2f, 0f, MaxZ - 0.1f), 180f, 3.0f);
            if (door != null) SnapToWall(door, Vector3.forward, MaxZ);

            Prop("drill_press_01", zone, new Vector3(-5.2f, 0f, MaxZ - 0.6f), 180f, 1.6f);
            Prop("portable_welding_cart", zone, new Vector3(-3.6f, 0f, MaxZ - 0.7f), 160f, 1.0f);
            Prop("propane_tank", zone, new Vector3(-2.6f, 0f, MaxZ - 0.45f), 0f, 0.6f);

            // The front-right corner (x ~ 6.2) is kept clear for the trash can.
            var desk = Prop("metal_office_desk", zone, new Vector3(4.3f, 0f, MaxZ - 0.55f), 180f, 0.76f);
            if (desk != null)
            {
                SnapToWall(desk, Vector3.forward, MaxZ);
                Prop("metal_toolbox", zone, new Vector3(3.9f, Top(desk, 0.76f), MaxZ - 0.5f), 170f, 0.3f);
            }

            var extinguisher = Prop("korean_fire_extinguisher_01", zone, new Vector3(-1.4f, 0f, MaxZ - 0.25f), 180f, 0.6f);
            if (extinguisher != null) SnapToWall(extinguisher, Vector3.forward, MaxZ);
        }

        static void BuildBackWall()
        {
            var zone = Group("Back Wall");
            Prop("steel_frame_shelves_02", zone, new Vector3(-4.5f, 0f, MinZ + 0.45f), 0f, 1.8f);
            Prop("cardboard_box_01", zone, new Vector3(-3.2f, 0f, MinZ + 0.5f), 15f, 0.35f);
            Prop("old_military_crate", zone, new Vector3(4.6f, 0f, MinZ + 0.5f), 5f, 0.45f);
            var pipes = Prop("modular_industrial_pipes_01", zone, new Vector3(0f, 3.3f, MinZ + 0.2f), 0f, null);
            if (pipes != null) SnapToWall(pipes, Vector3.back, MinZ);
        }

        static void BuildCeilingLamps()
        {
            var zone = Group("Ceiling Lamps");
            // Warm pools of light over each work zone; only two cast shadows (laptop GPU).
            HangingLamp(zone, new Vector3(-5.6f, 0f, 1.6f), shadows: true, intensity: 4f);
            HangingLamp(zone, new Vector3(5.4f, 0f, 2.6f), shadows: true, intensity: 3.2f);
            HangingLamp(zone, new Vector3(-3.6f, 0f, 6.9f), shadows: false, intensity: 3f);
            HangingLamp(zone, new Vector3(4.6f, 0f, 6.9f), shadows: false, intensity: 2.6f);
            HangingLamp(zone, new Vector3(0f, 0f, -3.8f), shadows: false, intensity: 2.2f);

            // Fluorescent tubes on the beams for a soft cool fill.
            foreach (var x in new[] { -3.5f, 3.5f })
            {
                var tube = Prop("mounted_fluorescent_lights", zone, new Vector3(x, 0f, 4.5f), 90f, null);
                if (tube != null) HangFromCeiling(tube, Height - 0.4f);
            }
            var fill = new GameObject("Fluorescent Fill").AddComponent<Light>();
            fill.transform.SetParent(zone, false);
            fill.transform.position = new Vector3(0f, Height - 0.7f, 3.5f);
            fill.type = LightType.Point;
            fill.color = new Color(0.82f, 0.9f, 1f);
            fill.intensity = 1.6f;
            fill.range = 9f;
            fill.shadows = LightShadows.None;
        }

        static void HangingLamp(Transform zone, Vector3 at, bool shadows, float intensity)
        {
            var lamp = Prop("hanging_industrial_lamp", zone, at, 0f, null);
            float bottom = Height - 1.1f;
            if (lamp != null)
            {
                HangFromCeiling(lamp, Height - 0.4f);
                bottom = Bounds(lamp).min.y;
            }
            var light = WarmLight("Lamp Light", zone, new Vector3(at.x, bottom - 0.05f, at.z), LightType.Spot, intensity, 7f, shadows);
            light.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            light.spotAngle = 95f;
            light.innerSpotAngle = 45f;
        }

        static void BuildReflectionProbe()
        {
            var probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(root, false);
            probe.transform.position = new Vector3(0f, 1.6f, 1.2f);
            probe.size = new Vector3(MaxX - MinX, Height, MaxZ - MinZ);
            probe.center = new Vector3(0f, Height / 2f - 1.6f, (MinZ + MaxZ) / 2f - 1.2f);
            probe.boxProjection = true;
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
        }

        // ------------------------------------------------------------------ placement helpers

        /// <summary>
        /// Places a model so the bottom-center of its bounds sits on 'at' after turning it by yaw.
        /// expectHeight: if the model comes in at a wildly different size (unit mix-ups), rescale it.
        /// </summary>
        static GameObject Prop(string id, Transform parent, Vector3 at, float yaw, float? expectHeight)
        {
            var model = PolyHavenLibrary.Model(id);
            if (model == null)
            {
                Debug.LogWarning($"[Cypher] Workshop prop '{id}' not found. Run: python3 tools/fetch_polyhaven.py");
                return null;
            }
            var holder = new GameObject(id);
            holder.transform.SetParent(parent, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
            PolyHavenLibrary.ApplyMaterials(instance, id);

            if (expectHeight.HasValue)
            {
                float h = Mathf.Max(0.001f, Bounds(holder).size.y);
                float ratio = expectHeight.Value / h;
                if (ratio > 3f || ratio < 1f / 3f) instance.transform.localScale *= ratio;
            }

            holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var b = Bounds(holder);
            holder.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);

            foreach (var r in holder.GetComponentsInChildren<Renderer>())
                if (b.size.magnitude < 0.5f) r.shadowCastingMode = ShadowCastingMode.Off; // small props: skip shadows
            return holder;
        }

        /// <summary>Slides an object so its side touches a wall's inner face (normal points into the wall).</summary>
        static void SnapToWall(GameObject go, Vector3 wallNormal, float wallCoordinate)
        {
            if (go == null) return;
            var b = Bounds(go);
            const float gap = 0.02f;
            if (wallNormal == Vector3.left) go.transform.position += Vector3.right * (wallCoordinate + gap - b.min.x);
            else if (wallNormal == Vector3.right) go.transform.position += Vector3.right * (wallCoordinate - gap - b.max.x);
            else if (wallNormal == Vector3.forward) go.transform.position += Vector3.forward * (wallCoordinate - gap - b.max.z);
            else if (wallNormal == Vector3.back) go.transform.position += Vector3.forward * (wallCoordinate + gap - b.min.z);
        }

        static void HangFromCeiling(GameObject go, float topY)
        {
            var b = Bounds(go);
            go.transform.position += Vector3.up * (topY - b.max.y);
        }

        /// <summary>Top surface height of a placed object (fallback if it's missing).</summary>
        static float Top(GameObject go, float fallback) => go != null ? Bounds(go).max.y : fallback;

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, bool castShadows = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        static Light WarmLight(string name, Transform parent, Vector3 position, LightType type, float intensity, float range, bool shadows)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.position = position;
            light.type = type;
            light.color = new Color(1f, 0.8f, 0.58f); // ~3000 K tungsten
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return light;
        }

        static Material Lit(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{PolyHavenLibrary.Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (!AssetDatabase.IsValidFolder($"{PolyHavenLibrary.Root}/Materials")) AssetDatabase.CreateFolder(PolyHavenLibrary.Root, "Materials");
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
