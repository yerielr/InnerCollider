using UnityEngine;

namespace MeshColliderTools
{
    /// <summary>
    /// Generates a double-sided MeshCollider by combining the original mesh with a
    /// flipped copy so physics contact is detected from both inside and outside.
    /// Essential for vent tunnels, hollow pipes, and concave walkable spaces.
    ///
    /// Editor workflow: use the Inspector buttons (Bake / Revert).
    /// Runtime workflow: enable <see cref="applyOnStart"/> for spawned objects.
    /// </summary>
    [AddComponentMenu("Physics/Double-Sided Mesh Collider")]
    [RequireComponent(typeof(MeshCollider))]
    public class DoubleSidedMeshCollider : MonoBehaviour
    {
        [Tooltip("Build the double-sided mesh in memory on Awake. For runtime-spawned objects only.")]
        public bool applyOnStart = false;

        [HideInInspector] public bool   isDoubleSided  = false;
        [HideInInspector] public Mesh   originalMeshRef  = null;
        [HideInInspector] public string savedAssetPath   = "";

        private MeshCollider _col;

        private void Awake()
        {
            _col = GetComponent<MeshCollider>();
            if (applyOnStart) ApplyInMemory();
        }

        /// <summary>Builds and assigns a double-sided collider mesh in memory (no disk write).</summary>
        public void ApplyInMemory()
        {
            if (_col == null) _col = GetComponent<MeshCollider>();

            Mesh source = ResolveSourceMesh();
            if (source == null)
            {
                Debug.LogError($"[DoubleSidedMeshCollider] No source mesh on '{gameObject.name}'.", this);
                return;
            }

            if (!isDoubleSided) originalMeshRef = source;

            Mesh ds = MeshAlgorithms.BuildDoubleSidedMesh(source);
            ds.name = source.name + "_doubleSided";

            _col.sharedMesh = ds;
            isDoubleSided   = true;
        }

        /// <summary>Restores the original mesh. For cross-session revert use the Inspector button.</summary>
        public void RevertInMemory()
        {
            if (_col == null) _col = GetComponent<MeshCollider>();

            Mesh target = originalMeshRef ?? GetComponent<MeshFilter>()?.sharedMesh;
            if (target == null)
            {
                Debug.LogWarning($"[DoubleSidedMeshCollider] Cannot revert '{gameObject.name}' — original mesh lost.", this);
                return;
            }

            _col.sharedMesh = target;
            isDoubleSided   = false;
            originalMeshRef = null;
            savedAssetPath  = "";
        }

        /// <summary>Returns the mesh to use as the bake source (MeshFilter takes priority).</summary>
        internal Mesh ResolveSourceMesh()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
            if (_col != null && _col.sharedMesh != null && !isDoubleSided) return _col.sharedMesh;
            return null;
        }
    }
}
