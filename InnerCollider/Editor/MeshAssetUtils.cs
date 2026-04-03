using UnityEngine;
using UnityEditor;
using System.IO;

namespace MeshColliderTools
{
    /// <summary>
    /// All AssetDatabase disk operations for the Mesh Collider Tools suite.
    /// Every write, delete, and load goes through here — nothing else in the
    /// codebase calls AssetDatabase directly for mesh I/O.
    /// </summary>
    internal static class MeshAssetUtils
    {
        // ── Save ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Saves <paramref name="mesh"/> as a .asset file inside the folder configured
        /// in Settings. Falls back to beside the source mesh if the configured folder
        /// is invalid, and to "Assets/GeneratedColliders" as a last resort.
        /// Returns the project-relative path, or empty string on failure.
        /// </summary>
        public static string SaveMeshAsset(Mesh mesh, Mesh sourceMesh)
        {
            if (mesh == null) return "";
            try
            {
                string dir = ResolveDirectory(sourceMesh);
                EnsureFolder(dir);
                string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{mesh.name}.asset");
                AssetDatabase.CreateAsset(mesh, path);
                AssetDatabase.SaveAssets();
                return path;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MeshColliderTools] Failed to save mesh '{mesh.name}': {e.Message}");
                return "";
            }
        }

        // ── Delete ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes the asset at <paramref name="path"/> if it exists.
        /// Safe to call with null or empty strings.
        /// </summary>
        public static void DeleteAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(Path.Combine(Application.dataPath, "../", path))) return;
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Attempts to find and delete a generated mesh asset by name when the stored
        /// path is unavailable. Searches the configured save folder only — never touches
        /// assets outside the MCT-managed directory.
        /// </summary>
        public static void DeleteAssetByName(string meshName)
        {
            if (string.IsNullOrEmpty(meshName)) return;

            string folder = MeshColliderToolsSettings.SaveFolder;
            if (!AssetDatabase.IsValidFolder(folder)) return;

            // Search only within the configured folder — never a project-wide scan.
            string[] guids = AssetDatabase.FindAssets($"{meshName} t:Mesh", new[] { folder });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Confirm the asset name matches exactly to avoid partial-name collisions.
                var candidate = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (candidate != null && candidate.name == meshName)
                {
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log($"[MeshColliderTools] Deleted orphaned asset '{path}'.");
                }
            }
            AssetDatabase.SaveAssets();
        }

        // ── Load ─────────────────────────────────────────────────────────────────

        /// <summary>Loads and returns a Mesh from <paramref name="path"/>, or null.</summary>
        public static Mesh LoadMesh(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        /// <summary>Pings a saved asset in the Project window so the user can find it.</summary>
        public static void PingAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var mesh = LoadMesh(path);
            if (mesh != null) EditorGUIUtility.PingObject(mesh);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Directory resolution order:
        ///   1. Configured SaveFolder (always preferred — explicit developer intent).
        ///   2. Folder containing the source mesh asset (if SaveFolder is invalid).
        ///   3. "Assets/GeneratedColliders" (hard fallback, should never be reached).
        /// </summary>
        private static string ResolveDirectory(Mesh sourceMesh)
        {
            string configured = MeshColliderToolsSettings.SaveFolder;
            if (!string.IsNullOrEmpty(configured)) return configured;

            // Fallback: beside the source mesh.
            if (sourceMesh != null)
            {
                string sp = AssetDatabase.GetAssetPath(sourceMesh);
                if (!string.IsNullOrEmpty(sp))
                    return Path.GetDirectoryName(sp)?.Replace("\\", "/") ?? "";
            }

            return "Assets/GeneratedColliders";
        }

        private static void EnsureFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir)) return;
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../", dir));
            AssetDatabase.Refresh();
        }
    }
}