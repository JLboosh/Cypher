using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cypher
{
    /// <summary>
    /// A paper-like copy of a hologram that can crumple. On creation it photographs the
    /// hologram (panel + text) into a texture with a temporary camera, then shows that picture
    /// on a finely subdivided plane drawn with the Cypher/HologramCrumple shader.
    /// </summary>
    public class CrumpleSheet : MonoBehaviour
    {
        const int SnapshotLayer = 31; // any layer the scene doesn't use; only the snapshot camera sees it
        const float PixelsPerMeter = 1400f;

        static readonly int CrumpleId = Shader.PropertyToID("_Crumple");
        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");

        Material material;
        Mesh mesh;
        RenderTexture snapshot;

        public float WorldHalfWidth { get; private set; }
        public float WorldHalfHeight { get; private set; }

        public static CrumpleSheet FromHologram(HologramPanel hologram, Shader crumpleShader, float ballRadiusMeters, float seed)
        {
            var src = hologram.transform;
            float scale = src.lossyScale.x;
            Vector2 size = hologram.PanelSize;

            var sheet = new GameObject("Crumpling " + hologram.name).AddComponent<CrumpleSheet>();
            sheet.transform.SetPositionAndRotation(src.position, src.rotation);
            sheet.transform.localScale = Vector3.one * scale;
            sheet.WorldHalfWidth = size.x * scale * 0.5f;
            sheet.WorldHalfHeight = size.y * scale * 0.5f;

            sheet.snapshot = Photograph(hologram, size * scale);
            sheet.mesh = BuildGrid(size, 40, 26);
            sheet.gameObject.AddComponent<MeshFilter>().sharedMesh = sheet.mesh;
            var renderer = sheet.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            sheet.material = new Material(crumpleShader);
            sheet.material.SetTexture("_MainTex", sheet.snapshot);
            sheet.material.SetVector("_HalfSize", new Vector4(size.x * 0.5f, size.y * 0.5f));
            sheet.material.SetFloat("_BallRadius", ballRadiusMeters / scale);
            sheet.material.SetFloat("_Seed", seed);
            renderer.sharedMaterial = sheet.material;
            sheet.SetCrumple(0f);
            return sheet;
        }

        public void SetCrumple(float amount) => material.SetFloat(CrumpleId, Mathf.Clamp01(amount));

        public void SetBrightness(float brightness) => material.SetFloat(BrightnessId, brightness);

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
            if (snapshot != null) snapshot.Release();
            if (snapshot != null) Destroy(snapshot);
        }

        /// <summary>Renders just this hologram, face-on, into an HDR texture.</summary>
        static RenderTexture Photograph(HologramPanel hologram, Vector2 worldSize)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(worldSize.x * PixelsPerMeter), 64, 1024);
            int h = Mathf.Clamp(Mathf.RoundToInt(worldSize.y * PixelsPerMeter), 64, 1024);
            var rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGBHalf) { name = "Hologram Snapshot" };
            rt.Create();

            var camGo = new GameObject("Snapshot Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = worldSize.y * 0.5f;
            cam.aspect = worldSize.x / worldSize.y;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << SnapshotLayer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1f;
            cam.targetTexture = rt;
            var t = hologram.transform;
            camGo.transform.SetPositionAndRotation(t.position - t.forward * 0.3f, t.rotation);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;

            // Text that was just re-shown (e.g. after the edit panel closed) may not have rebuilt yet.
            foreach (var text in hologram.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();

            // Temporarily move the hologram's visible parts onto the snapshot layer.
            var renderers = hologram.GetComponentsInChildren<Renderer>();
            var oldLayers = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                oldLayers[i] = renderers[i].gameObject.layer;
                if (!(renderers[i] is ParticleSystemRenderer)) renderers[i].gameObject.layer = SnapshotLayer;
            }

            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
                RenderPipeline.SubmitRenderRequest(cam, request);
            else
                cam.Render();

            for (int i = 0; i < renderers.Length; i++) renderers[i].gameObject.layer = oldLayers[i];
            Destroy(camGo);
            return rt;
        }

        static Mesh BuildGrid(Vector2 size, int nx, int ny)
        {
            var verts = new Vector3[(nx + 1) * (ny + 1)];
            var uvs = new Vector2[verts.Length];
            for (int y = 0; y <= ny; y++)
            for (int x = 0; x <= nx; x++)
            {
                float u = x / (float)nx, v = y / (float)ny;
                int i = y * (nx + 1) + x;
                verts[i] = new Vector3((u - 0.5f) * size.x, (v - 0.5f) * size.y, 0f);
                uvs[i] = new Vector2(u, v);
            }

            var tris = new int[nx * ny * 6];
            int k = 0;
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int i = y * (nx + 1) + x;
                tris[k++] = i; tris[k++] = i + nx + 1; tris[k++] = i + 1;
                tris[k++] = i + 1; tris[k++] = i + nx + 1; tris[k++] = i + nx + 2;
            }

            var mesh = new Mesh { name = "Crumple Grid" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            // The shader moves vertices up to ~half a sheet toward the camera; keep it from being culled.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size.x, size.y, size.x) * 1.5f);
            return mesh;
        }
    }
}
