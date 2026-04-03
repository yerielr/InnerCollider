using UnityEngine;
using UnityEditor;

namespace MeshColliderTools
{
    /// <summary>
    /// Persistent per-machine settings for Mesh Collider Tools, backed by EditorPrefs.
    /// </summary>
    internal static class MeshColliderToolsSettings
    {
        private const string k_SaveFolder  = "MCT.SaveFolder";
        private const string k_ShowGizmos  = "MCT.ShowGizmos";
        private const string k_AutoAddComp = "MCT.AutoAddComp";
        private const string k_SuffixDS    = "MCT.SuffixDS";
        private const string k_WarnTris    = "MCT.WarnTris";
        private const string k_ErrorTris   = "MCT.ErrorTris";

        /// <summary>Fallback folder when the source mesh has no resolvable asset path.</summary>
        public static string SaveFolder
        {
            get => EditorPrefs.GetString(k_SaveFolder, "Assets/GeneratedColliders");
            set => EditorPrefs.SetString(k_SaveFolder, value);
        }

        /// <summary>Draw scene-view gizmos for selected objects with an active bake.</summary>
        public static bool ShowGizmos
        {
            get => EditorPrefs.GetBool(k_ShowGizmos, true);
            set => EditorPrefs.SetBool(k_ShowGizmos, value);
        }

        /// <summary>Automatically add a DoubleSidedMeshCollider component when baking from the window.</summary>
        public static bool AutoAddComponent
        {
            get => EditorPrefs.GetBool(k_AutoAddComp, true);
            set => EditorPrefs.SetBool(k_AutoAddComp, value);
        }

        /// <summary>Suffix appended to the filename when saving double-sided mesh assets.</summary>
        public static string SuffixDoubleSided
        {
            get => EditorPrefs.GetString(k_SuffixDS, "_doubleSided");
            set => EditorPrefs.SetString(k_SuffixDS, value);
        }

        /// <summary>Triangle count at which a collider is flagged amber in the window.</summary>
        public static int WarnTriCount
        {
            get => EditorPrefs.GetInt(k_WarnTris, 10_000);
            set => EditorPrefs.SetInt(k_WarnTris, Mathf.Max(1, value));
        }

        /// <summary>Triangle count at which a collider is flagged red in the window.</summary>
        public static int ErrorTriCount
        {
            get => EditorPrefs.GetInt(k_ErrorTris, 50_000);
            set => EditorPrefs.SetInt(k_ErrorTris, Mathf.Max(1, value));
        }

        public static void ResetToDefaults()
        {
            EditorPrefs.DeleteKey(k_SaveFolder);
            EditorPrefs.DeleteKey(k_ShowGizmos);
            EditorPrefs.DeleteKey(k_AutoAddComp);
            EditorPrefs.DeleteKey(k_SuffixDS);
            EditorPrefs.DeleteKey(k_WarnTris);
            EditorPrefs.DeleteKey(k_ErrorTris);
        }
    }
}