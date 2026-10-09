using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Where the hologram with a given rank should float.
    /// The player sits at the world origin looking down +Z.
    ///   Ranks 0-2: front row at eye level.
    ///   Ranks 3-4: second row, lower, between the front ones.
    ///   Ranks 5+ : a larger ring further back, alternating left/right, scaled up to stay readable.
    /// Phase 6 feeds this the smart ranking; for now rank = list order.
    /// </summary>
    public static class HologramLayout
    {
        public struct Slot
        {
            public Vector3 position;
            public float scale;
        }

        static readonly float[] FrontAngles = { 0f, -26f, 26f };
        static readonly float[] SecondAngles = { -13f, 13f };

        public const int FrontCount = 5;

        public static Slot GetSlot(int rank)
        {
            if (rank < 3) return Make(FrontAngles[rank], 1.0f, 1.30f, 1f);
            if (rank < FrontCount) return Make(SecondAngles[rank - 3], 1.05f, 1.02f, 1f);

            int k = rank - FrontCount;
            float side = k % 2 == 0 ? -1f : 1f;
            int index = k / 2;
            int column = index % 3;
            int row = index / 3;
            float angle = side * (42f + column * 17f);
            float height = 1.62f - (row % 3) * 0.32f;
            float radius = 1.75f + (row / 3) * 0.5f;
            return Make(angle, radius, height, 1.25f);
        }

        static Slot Make(float angleDeg, float radius, float height, float scale)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Slot
            {
                position = new Vector3(Mathf.Sin(a) * radius, height, Mathf.Cos(a) * radius),
                scale = scale,
            };
        }
    }
}
