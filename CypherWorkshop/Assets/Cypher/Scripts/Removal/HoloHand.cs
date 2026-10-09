using System.Collections.Generic;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// A translucent holographic hand built from capsules: palm, four 3-joint fingers, a 2-joint
    /// thumb and a wrist stub. Local axes: fingers point +Y, the palm faces +Z, the wrist is at
    /// the origin. A left hand is the same hand mirrored on X.
    /// </summary>
    public class HoloHand : MonoBehaviour
    {
        // (x offset, segment lengths, thickness) for index, middle, ring, little finger.
        static readonly (float x, float[] lengths, float thickness)[] Fingers =
        {
            (0.027f, new[] { 0.038f, 0.024f, 0.020f }, 0.017f),
            (0.009f, new[] { 0.042f, 0.027f, 0.021f }, 0.018f),
            (-0.009f, new[] { 0.039f, 0.025f, 0.020f }, 0.017f),
            (-0.026f, new[] { 0.030f, 0.020f, 0.018f }, 0.015f),
        };
        static readonly float[] CurlAngles = { 62f, 85f, 60f };
        static readonly float[] ThumbLengths = { 0.036f, 0.028f };

        readonly List<Transform> fingerJoints = new List<Transform>(); // 3 per finger, in order
        readonly List<Transform> thumbJoints = new List<Transform>();
        Material material;
        static readonly int MaterializeId = Shader.PropertyToID("_Materialize");

        public static HoloHand Create(string name, bool left, Material source)
        {
            var hand = new GameObject(name).AddComponent<HoloHand>();
            hand.material = new Material(source);
            hand.Build();
            if (left) hand.transform.localScale = new Vector3(-1f, 1f, 1f);
            hand.SetMaterialize(0f);
            return hand;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        public void SetMaterialize(float amount) => material.SetFloat(MaterializeId, amount);

        /// <summary>0 = open flat hand, 1 = fingers curled around something.</summary>
        public void SetCurl(float curl)
        {
            for (int i = 0; i < fingerJoints.Count; i++)
                fingerJoints[i].localRotation = Quaternion.Euler(CurlAngles[i % 3] * curl, 0f, 0f);
            thumbJoints[0].localRotation = Quaternion.Euler(25f * curl, -20f * curl, -40f + 25f * curl); // splayed outward, closing in
            thumbJoints[1].localRotation = Quaternion.Euler(45f * curl, 0f, 0f);
        }

        public void SetPose(Vector3 wrist, Vector3 palmNormal, Vector3 fingerDirection)
        {
            transform.SetPositionAndRotation(wrist, Quaternion.LookRotation(palmNormal, fingerDirection));
        }

        void Build()
        {
            Capsule("Wrist", transform, new Vector3(0f, -0.03f, 0f), new Vector3(0.05f, 0.03f, 0.032f));
            Capsule("Palm", transform, new Vector3(0f, 0.045f, 0f), new Vector3(0.078f, 0.047f, 0.026f));

            foreach (var f in Fingers)
            {
                Transform parent = transform;
                Vector3 at = new Vector3(f.x, 0.085f, 0.002f);
                for (int s = 0; s < 3; s++)
                {
                    var joint = new GameObject("Joint").transform;
                    joint.SetParent(parent, false);
                    joint.localPosition = at;
                    fingerJoints.Add(joint);
                    float len = f.lengths[s];
                    Capsule("Bone", joint, new Vector3(0f, len * 0.5f, 0f), new Vector3(f.thickness, len * 0.5f + f.thickness * 0.5f, f.thickness));
                    parent = joint;
                    at = new Vector3(0f, len, 0f);
                }
            }

            Transform tParent = transform;
            Vector3 tAt = new Vector3(0.036f, 0.022f, 0.008f); // thumb on +X (right hand)
            for (int s = 0; s < 2; s++)
            {
                var joint = new GameObject("Thumb Joint").transform;
                joint.SetParent(tParent, false);
                joint.localPosition = tAt;
                thumbJoints.Add(joint);
                float len = ThumbLengths[s];
                Capsule("Thumb", joint, new Vector3(0f, len * 0.5f, 0f), new Vector3(0.02f, len * 0.5f + 0.01f, 0.02f));
                tParent = joint;
                tAt = new Vector3(0f, len, 0f);
            }
            SetCurl(0f);
        }

        void Capsule(string name, Transform parent, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            // The capsule primitive is 1 wide and 2 tall.
            go.transform.localScale = new Vector3(size.x, size.y, size.z);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
