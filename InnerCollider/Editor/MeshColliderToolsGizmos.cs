using UnityEditor;
using UnityEngine;

namespace MeshColliderTools
{
    /// <summary>
    /// Scene-view overlay for Mesh Collider Tools.
    /// Draws wireframe bounds for selected objects that have a DoubleSidedMeshCollider component.
    /// Can be toggled in the Settings tab of the Mesh Collider Tools window.
    /// </summary>
    [InitializeOnLoad]
    public static class MeshColliderToolsGizmos
    {
        private static readonly Color k_DSColor = new Color(0.25f, 0.85f, 1f, 0.5f);   // cyan
        private static readonly Color k_LabelBg = new Color(0f, 0f, 0f, 0.55f);

        static MeshColliderToolsGizmos()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sv)
        {
            if (!MeshColliderToolsSettings.ShowGizmos) return;
            if (Selection.gameObjects == null) return;

            foreach (var go in Selection.gameObjects)
            {
                DrawForObject(go);
                // One level of children so vent meshes inside parent rigs are covered.
                foreach (Transform child in go.transform)
                    DrawForObject(child.gameObject);
            }
        }

        private static void DrawForObject(GameObject go)
        {
            var col = go.GetComponent<MeshCollider>();
            var ds = go.GetComponent<DoubleSidedMeshCollider>();

            if (col == null) return;
            if (ds == null || !ds.isDoubleSided) return;

            Handles.matrix = go.transform.localToWorldMatrix;

            Handles.color = k_DSColor;
            DrawWireBounds(col.sharedMesh.bounds);
            DrawFloatingLabel(go, col.sharedMesh.bounds, "Double-Sided");

            Handles.matrix = Matrix4x4.identity;
        }

        private static void DrawFloatingLabel(GameObject go, Bounds b, string text)
        {
            Vector3 worldPos = go.transform.TransformPoint(
                b.center + Vector3.up * b.extents.y * 1.3f);

            Handles.BeginGUI();
            Vector2 guiPos = HandleUtility.WorldToGUIPoint(worldPos);
            var content = new GUIContent($"  {text}  ");
            var size = EditorStyles.miniLabel.CalcSize(content);
            var rect = new Rect(guiPos.x - size.x * 0.5f, guiPos.y - size.y, size.x, size.y);
            EditorGUI.DrawRect(rect, k_LabelBg);
            GUI.Label(rect, content, EditorStyles.miniLabel);
            Handles.EndGUI();
        }

        private static void DrawWireBounds(Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            Vector3[] v =
            {
                c + new Vector3(-e.x, -e.y, -e.z),
                c + new Vector3( e.x, -e.y, -e.z),
                c + new Vector3( e.x, -e.y,  e.z),
                c + new Vector3(-e.x, -e.y,  e.z),
                c + new Vector3(-e.x,  e.y, -e.z),
                c + new Vector3( e.x,  e.y, -e.z),
                c + new Vector3( e.x,  e.y,  e.z),
                c + new Vector3(-e.x,  e.y,  e.z),
            };
            // Bottom face
            Handles.DrawLine(v[0], v[1]); Handles.DrawLine(v[1], v[2]);
            Handles.DrawLine(v[2], v[3]); Handles.DrawLine(v[3], v[0]);
            // Top face
            Handles.DrawLine(v[4], v[5]); Handles.DrawLine(v[5], v[6]);
            Handles.DrawLine(v[6], v[7]); Handles.DrawLine(v[7], v[4]);
            // Verticals
            Handles.DrawLine(v[0], v[4]); Handles.DrawLine(v[1], v[5]);
            Handles.DrawLine(v[2], v[6]); Handles.DrawLine(v[3], v[7]);
        }
    }
}