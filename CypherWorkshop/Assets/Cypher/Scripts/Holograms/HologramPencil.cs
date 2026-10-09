using UnityEngine;

namespace Cypher
{
    /// <summary>Marks the small pencil icon on a hologram. Clicking it opens the edit panel.</summary>
    public class HologramPencil : MonoBehaviour
    {
        public HologramPanel Owner => GetComponentInParent<HologramPanel>();
    }
}
