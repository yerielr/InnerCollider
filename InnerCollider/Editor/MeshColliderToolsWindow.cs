using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;

namespace MeshColliderTools
{
    /// <summary>
    /// InnerCollider — Tools → InnerCollider (Ctrl+Shift+M)
    /// Bakes double-sided collider mesh assets so MeshColliders detect from both inside and outside.
    /// </summary>
    public class MeshColliderToolsWindow : EditorWindow
    {
        private const string k_Version = "1.0.0";

        private enum Tab { DoubleSided, Settings }
        private Tab    _activeTab = Tab.DoubleSided;
        private static readonly string[] k_TabLabels = { "Double-Sided", "Settings" };

        private Vector2 _scroll;
        private bool    _autoChildren = true;

        private List<MeshCollider>               _colliders  = new();
        private Dictionary<MeshCollider, bool>   _foldouts   = new();
        private Dictionary<MeshCollider, Mesh>   _bakedOrig  = new();
        private Dictionary<MeshCollider, string> _assetPaths = new();

        // ── Cached per-selection stats ────────────────────────────────────────────
        // Populated in RefreshColliders so draw calls never allocate.
        private struct RowStats
        {
            public int  current, original, projected;
            public bool isBaked;
        }
        private Dictionary<MeshCollider, RowStats> _rowStats = new();
        private int _totalCurrent, _totalOriginal, _totalProjected, _bakedCount;

        // ── Styles / icon ─────────────────────────────────────────────────────────
        private GUIStyle  _headerStyle;
        private bool      _stylesReady;
        private Texture2D _icon;
        private bool      _iconSearched;

        private bool IsPlayMode =>
            EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Tools/InnerCollider  %#m")]
        public static void ShowWindow()
        {
            var w = GetWindow<MeshColliderToolsWindow>("InnerCollider");
            w.minSize = new Vector2(380, 460);
            w.Show();
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnDisable() => Selection.selectionChanged -= OnSelectionChanged;

        private void OnSelectionChanged()
        {
            RefreshColliders();
            Repaint();
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();

            if (IsPlayMode)
            {
                EditorGUILayout.HelpBox("Disabled in Play Mode.", MessageType.Warning);
                return;
            }

            int next = GUILayout.Toolbar((int)_activeTab, k_TabLabels, GUILayout.Height(24));
            if (next != (int)_activeTab) _activeTab = (Tab)next;

            EditorGUILayout.Space(2);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawSelectionBar();
            EditorGUILayout.Space(2);

            if (_activeTab == Tab.DoubleSided) DrawDoubleSidedTab();
            else                               DrawSettingsTab();

            EditorGUILayout.Space(2);
            DrawColliderList();

            EditorGUILayout.Space();
            EditorGUILayout.EndScrollView();
        }

        // ─── Header ───────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            EnsureIcon();

            var rect = EditorGUILayout.GetControlRect(false, 36);
            EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.16f));

            float x = rect.x + 8f;

            // Logo — loaded once from the same folder as this script.
            // Place icon.png (exported from icon_svg.svg) beside MeshColliderToolsWindow.cs.
            if (_icon != null)
            {
                GUI.DrawTexture(new Rect(x, rect.y + 6f, 24f, 24f), _icon,
                    ScaleMode.ScaleToFit, alphaBlend: true);
                x += 30f;
            }

            GUI.Label(new Rect(x, rect.y + 4f,  rect.width - x - 56f, 18f), "InnerCollider", _headerStyle);
            GUI.Label(new Rect(x, rect.y + 22f, rect.width - x - 56f, 12f),
                "Double-sided collision baking", EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.xMax - 54f, rect.y + 4f, 50f, 14f),
                $"v{k_Version}", EditorStyles.miniLabel);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal   = { textColor = Color.white }
            };
            _stylesReady = true;
        }

        /// <summary>
        /// Locates icon.png next to this script file in the project.
        /// Runs once; result cached in _icon.
        /// </summary>
        private void EnsureIcon()
        {
            if (_iconSearched) return;
            _iconSearched = true;

            string[] guids = AssetDatabase.FindAssets($"t:Script {nameof(MeshColliderToolsWindow)}");
            if (guids.Length == 0) return;

            string dir = Path.GetDirectoryName(
                AssetDatabase.GUIDToAssetPath(guids[0]))?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir)) return;

            // Prefer icon.png; also try icon_svg.png if the SVG was exported with that name.
            _icon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/icon.png")
                 ?? AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/icon_svg.png");
        }

        // ─── Selection Bar ────────────────────────────────────────────────────────

        /// <summary>
        /// Compact single-row status bar: toggle · info summary · projected tris · refresh.
        /// Replaces the previous titled section box.
        /// </summary>
        private void DrawSelectionBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Children toggle — responds immediately.
            bool prevAuto = _autoChildren;
            _autoChildren = EditorGUILayout.ToggleLeft("Children", _autoChildren, GUILayout.Width(70));
            if (_autoChildren != prevAuto) { RefreshColliders(); Repaint(); }

            // Divider
            var prevC = GUI.contentColor;
            GUI.contentColor = new Color(1f, 1f, 1f, 0.18f);
            GUILayout.Label("|", EditorStyles.miniLabel, GUILayout.Width(8));
            GUI.contentColor = prevC;

            if (_colliders.Count == 0)
            {
                GUILayout.Label("Select GameObjects with MeshColliders", EditorStyles.miniLabel);
            }
            else
            {
                // "4 colliders · 2 baked"
                string info = $"{_colliders.Count} collider{(_colliders.Count == 1 ? "" : "s")}";
                if (_bakedCount > 0) info += $"  ·  {_bakedCount} baked";
                GUILayout.Label(info, EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                // Colour-coded projected tri total.
                Color projColor = _totalProjected >= MeshColliderToolsSettings.ErrorTriCount
                    ? new Color(1f, 0.35f, 0.35f)
                    : _totalProjected >= MeshColliderToolsSettings.WarnTriCount
                        ? new Color(1f, 0.78f, 0.2f)
                        : new Color(0.55f, 1f, 0.55f);

                GUI.contentColor = new Color(1f, 1f, 1f, 0.35f);
                GUILayout.Label("→", EditorStyles.miniLabel, GUILayout.Width(12));
                GUI.contentColor = projColor;
                GUILayout.Label($"{_totalProjected:N0}t", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                GUI.contentColor = prevC;
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("⟳", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(16)))
            {
                RefreshColliders();
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── Refresh / Cache ──────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the collider list and caches all tri stats.
        /// Called on selection change and after every bake/revert — never from draw paths.
        /// Uses GetIndexCount to avoid the triangle-array allocation of mesh.triangles.
        /// </summary>
        private void RefreshColliders()
        {
            _colliders.Clear();
            _foldouts.Clear();
            _rowStats.Clear();

            if (Selection.gameObjects != null)
            {
                foreach (var go in Selection.gameObjects)
                {
                    var src = _autoChildren
                        ? go.GetComponentsInChildren<MeshCollider>(true)
                        : go.GetComponent<MeshCollider>() is { } c
                            ? new[] { c }
                            : System.Array.Empty<MeshCollider>();

                    foreach (var mc in src)
                        if (!_colliders.Contains(mc)) _colliders.Add(mc);
                }

                foreach (var col in _colliders)
                {
                    if (col == null) continue;
                    var  ds      = col.GetComponent<DoubleSidedMeshCollider>();
                    bool isBaked = ds != null && ds.isDoubleSided;
                    int  cur     = TriCount(col.sharedMesh);
                    int  orig    = (isBaked && ds.originalMeshRef != null)
                                       ? TriCount(ds.originalMeshRef) : cur;
                    _rowStats[col] = new RowStats
                    {
                        current   = cur,
                        original  = orig,
                        projected = isBaked ? cur : cur * 2,
                        isBaked   = isBaked
                    };
                }
            }

            // Aggregate totals without LINQ to keep allocations minimal.
            _totalCurrent = _totalOriginal = _totalProjected = _bakedCount = 0;
            foreach (var s in _rowStats.Values)
            {
                _totalCurrent   += s.current;
                _totalOriginal  += s.original;
                _totalProjected += s.projected;
                if (s.isBaked) _bakedCount++;
            }
        }

        /// <summary>
        /// Non-allocating tri count. Sums across all submeshes via GetIndexCount,
        /// which avoids copying the full triangle array as mesh.triangles would.
        /// </summary>
        private static int TriCount(Mesh m)
        {
            if (m == null) return 0;
            int n = 0;
            for (int i = 0; i < m.subMeshCount; i++)
                n += (int)(m.GetIndexCount(i) / 3);
            return n;
        }

        // ─── Double-Sided Tab ─────────────────────────────────────────────────────

        private void DrawDoubleSidedTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Double-Sided Collider", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox(
                "MeshColliders are one-sided — objects inside a mesh pass straight through. " +
                "Baking duplicates the mesh with flipped winding so physics detects from both surfaces. " +
                "Triangle count doubles; for complex static geometry consider primitive colliders.",
                MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            bool hasColliders = _colliders.Count > 0;
            using (new EditorGUI.DisabledScope(!hasColliders))
            {
                if (GUILayout.Button($"Bake Double-Sided → {_colliders.Count} Collider(s)", GUILayout.Height(32)))
                    ApplyDoubleSided();

                EditorGUILayout.Space(2);

                if (GUILayout.Button("Revert All", GUILayout.Height(24)))
                    RevertAll();
            }
        }

        // ─── Settings Tab ─────────────────────────────────────────────────────────

        private void DrawSettingsTab()
        {
            // ── Save Location ─────────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Save Location", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assets are saved beside the source mesh. The fallback folder is used for procedural meshes.",
                MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Fallback Folder", GUILayout.Width(100));
            MeshColliderToolsSettings.SaveFolder = EditorGUILayout.TextField(MeshColliderToolsSettings.SaveFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(55)))
            {
                string sel = EditorUtility.SaveFolderPanel("Select Save Folder",
                    Application.dataPath, "GeneratedColliders");
                if (!string.IsNullOrEmpty(sel) && sel.StartsWith(Application.dataPath))
                    MeshColliderToolsSettings.SaveFolder =
                        "Assets" + sel.Substring(Application.dataPath.Length);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
            GUILayout.Label("Suffix", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Double-Sided", GUILayout.Width(100));
            string s = EditorGUILayout.TextField(MeshColliderToolsSettings.SuffixDoubleSided);
            if (!string.IsNullOrWhiteSpace(s)) MeshColliderToolsSettings.SuffixDoubleSided = s;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // ── Performance Thresholds ────────────────────────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Performance Thresholds", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Triangle counts checked in the collider list. These are collider mesh tris — not render mesh tris.",
                MessageType.None);
            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("⬤  Warn (amber)", "Flag colliders above this count in orange."),
                GUILayout.Width(130));
            int warn = EditorGUILayout.DelayedIntField(MeshColliderToolsSettings.WarnTriCount);
            if (warn > 0 && warn < MeshColliderToolsSettings.ErrorTriCount)
                MeshColliderToolsSettings.WarnTriCount = warn;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("⬤  Error (red)", "Flag colliders above this count in red."),
                GUILayout.Width(130));
            int error = EditorGUILayout.DelayedIntField(MeshColliderToolsSettings.ErrorTriCount);
            if (error > MeshColliderToolsSettings.WarnTriCount)
                MeshColliderToolsSettings.ErrorTriCount = error;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            var prevColor = GUI.contentColor;
            GUI.contentColor = new Color(1f, 0.78f, 0.2f);
            EditorGUILayout.LabelField(
                $"Amber  ≥ {MeshColliderToolsSettings.WarnTriCount:N0} tris", EditorStyles.miniLabel);
            GUI.contentColor = new Color(1f, 0.35f, 0.35f);
            EditorGUILayout.LabelField(
                $"Red  ≥ {MeshColliderToolsSettings.ErrorTriCount:N0} tris", EditorStyles.miniLabel);
            GUI.contentColor = prevColor;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // ── Scene View ────────────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Scene View", EditorStyles.boldLabel);
            MeshColliderToolsSettings.ShowGizmos =
                EditorGUILayout.Toggle("Show Gizmos", MeshColliderToolsSettings.ShowGizmos);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // ── Behaviour ─────────────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Behaviour", EditorStyles.boldLabel);
            MeshColliderToolsSettings.AutoAddComponent = EditorGUILayout.Toggle(
                new GUIContent("Auto-add Component",
                    "Adds DoubleSidedMeshCollider for cross-session revert."),
                MeshColliderToolsSettings.AutoAddComponent);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // ── Reset ─────────────────────────────────────────────────────────────
            if (GUILayout.Button("Reset All Settings to Defaults", GUILayout.Height(24)))
            {
                if (EditorUtility.DisplayDialog("Reset Settings",
                        "Restore factory defaults?", "Reset", "Cancel"))
                    MeshColliderToolsSettings.ResetToDefaults();
            }
        }

        // ─── Collider List ────────────────────────────────────────────────────────

        private static int WarnTris  => MeshColliderToolsSettings.WarnTriCount;
        private static int ErrorTris => MeshColliderToolsSettings.ErrorTriCount;

        private void DrawColliderList()
        {
            if (_colliders.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── List header ───────────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Colliders in Selection", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (_bakedCount > 0)
                EditorGUILayout.LabelField($"{_bakedCount} baked",
                    EditorStyles.miniBoldLabel, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            DrawTriStats();
            EditorGUILayout.Space(4);

            // ── Per-collider rows ─────────────────────────────────────────────────
            foreach (var col in _colliders)
                if (col != null) DrawColliderRow(col);

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Compact single-row aggregate: Current | Original | After Bake.
        /// Uses cached totals — no per-frame allocation.
        /// </summary>
        private void DrawTriStats()
        {
            EditorGUILayout.BeginHorizontal(
                new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(6, 6, 3, 3) });

            var prevC = GUI.contentColor;

            EditorGUILayout.LabelField("Current",    EditorStyles.miniLabel,    GUILayout.Width(44));
            EditorGUILayout.LabelField(_totalCurrent.ToString("N0"),
                EditorStyles.miniBoldLabel, GUILayout.Width(60));

            GUI.contentColor = new Color(1f, 1f, 1f, 0.18f);
            GUILayout.Label("│", EditorStyles.miniLabel, GUILayout.Width(8));
            GUI.contentColor = prevC;

            EditorGUILayout.LabelField("Original",   EditorStyles.miniLabel,    GUILayout.Width(48));
            EditorGUILayout.LabelField(_totalOriginal.ToString("N0"),
                EditorStyles.miniLabel, GUILayout.Width(60));

            GUI.contentColor = new Color(1f, 1f, 1f, 0.18f);
            GUILayout.Label("│", EditorStyles.miniLabel, GUILayout.Width(8));
            GUI.contentColor = prevC;

            EditorGUILayout.LabelField("After Bake", EditorStyles.miniLabel,    GUILayout.Width(58));
            GUI.contentColor = _totalProjected >= ErrorTris ? new Color(1f, 0.35f, 0.35f)
                             : _totalProjected >= WarnTris  ? new Color(1f, 0.78f, 0.2f)
                             : new Color(0.55f, 1f, 0.55f);
            EditorGUILayout.LabelField(_totalProjected.ToString("N0"), EditorStyles.miniBoldLabel);
            GUI.contentColor = prevC;

            EditorGUILayout.EndHorizontal();

            if (_totalProjected >= ErrorTris)
                EditorGUILayout.HelpBox(
                    $"Projected tri count ({_totalProjected:N0}) is very high. " +
                    "Consider primitive colliders.", MessageType.Warning);
            else if (_totalProjected >= WarnTris)
                EditorGUILayout.HelpBox(
                    $"Projected tri count ({_totalProjected:N0}) is moderately high. " +
                    "Monitor physics performance.", MessageType.None);
        }

        private void DrawColliderRow(MeshCollider col)
        {
            if (!_rowStats.TryGetValue(col, out var stats)) return;
            if (!_foldouts.ContainsKey(col)) _foldouts[col] = false;

            int  currentTris   = stats.current;
            int  originalTris  = stats.original;
            int  projectedTris = stats.projected;
            bool isBaked       = stats.isBaked;

            // Path: prefer window-session dict (set on bake), fall back to component field.
            if (!_assetPaths.TryGetValue(col, out string path) || string.IsNullOrEmpty(path))
                path = col.GetComponent<DoubleSidedMeshCollider>()?.savedAssetPath ?? "";

            // ── Summary row ───────────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();

            _foldouts[col] = EditorGUILayout.Foldout(_foldouts[col], GUIContent.none, true,
                new GUIStyle(EditorStyles.foldout) { fixedWidth = 12 });

            EditorGUILayout.ObjectField(col.gameObject, typeof(GameObject), true,
                GUILayout.MinWidth(80));

            // Status badge
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = isBaked
                ? new Color(0.13f, 0.45f, 0.2f)
                : new Color(0.3f,  0.3f,  0.3f);
            GUILayout.Label(isBaked ? "BAKED" : "ORIG",
                EditorStyles.miniBoldLabel, GUILayout.Width(44));
            GUI.backgroundColor = prevBg;

            // ── Inline tri display ────────────────────────────────────────────────
            // Baked:   "12,800t  (6,400)"   current · dim original
            // Unbaked: "6,400  →  12,800t"  current → colour-coded projected
            var prevContent = GUI.contentColor;
            Color curColor  = currentTris >= ErrorTris ? new Color(1f, 0.35f, 0.35f)
                            : currentTris >= WarnTris  ? new Color(1f, 0.78f, 0.2f)
                            : prevContent;

            if (isBaked)
            {
                GUI.contentColor = curColor;
                GUILayout.Label($"{currentTris:N0}t", EditorStyles.miniLabel, GUILayout.Width(52));
                GUI.contentColor = new Color(prevContent.r, prevContent.g, prevContent.b, 0.42f);
                GUILayout.Label($"({originalTris:N0})", EditorStyles.miniLabel, GUILayout.Width(48));
            }
            else
            {
                Color projColor = projectedTris >= ErrorTris ? new Color(1f, 0.35f, 0.35f)
                                : projectedTris >= WarnTris  ? new Color(1f, 0.78f, 0.2f)
                                : new Color(0.55f, 1f, 0.55f);

                GUI.contentColor = curColor;
                GUILayout.Label($"{currentTris:N0}", EditorStyles.miniLabel, GUILayout.Width(40));
                GUI.contentColor = new Color(1f, 1f, 1f, 0.32f);
                GUILayout.Label("→", EditorStyles.miniLabel, GUILayout.Width(14));
                GUI.contentColor = projColor;
                GUILayout.Label($"{projectedTris:N0}t", EditorStyles.miniLabel, GUILayout.Width(46));
            }
            GUI.contentColor = prevContent;

            // ── Action buttons ────────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(path))
            {
                if (GUILayout.Button(new GUIContent("↗", "Ping asset in Project window"),
                    EditorStyles.miniButton, GUILayout.Width(22)))
                    MeshAssetUtils.PingAsset(path);
            }
            else
            {
                GUILayout.Space(26);
            }

            var ds = col.GetComponent<DoubleSidedMeshCollider>();
            if (isBaked && GUILayout.Button(new GUIContent("↩", "Revert to original mesh"),
                EditorStyles.miniButton, GUILayout.Width(22)))
                RevertSingle(col, ds);

            EditorGUILayout.EndHorizontal();

            // ── Foldout detail ────────────────────────────────────────────────────
            // Row-level bake status visible inline; detail shows breakdown + asset refs.
            if (_foldouts[col])
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawStatRow("Original",   originalTris,  GUI.contentColor);
                DrawStatRow("Current",    currentTris,
                    currentTris >= ErrorTris ? new Color(1f, 0.35f, 0.35f) :
                    currentTris >= WarnTris  ? new Color(1f, 0.78f, 0.2f)  : GUI.contentColor);
                DrawStatRow("After Bake", projectedTris,
                    projectedTris >= ErrorTris ? new Color(1f, 0.35f, 0.35f) :
                    projectedTris >= WarnTris  ? new Color(1f, 0.78f, 0.2f)  :
                    new Color(0.55f, 1f, 0.55f));
                EditorGUILayout.EndVertical();

                if (col.sharedMesh != null)
                    EditorGUILayout.LabelField("Vertices",
                        col.sharedMesh.vertexCount.ToString("N0"), EditorStyles.miniLabel);

                EditorGUILayout.Space(2);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Collider", col,            typeof(MeshCollider), true);
                    if (col.sharedMesh != null)
                        EditorGUILayout.ObjectField("Mesh",   col.sharedMesh, typeof(Mesh),         false);
                    if (!string.IsNullOrEmpty(path))
                    {
                        var saved = MeshAssetUtils.LoadMesh(path);
                        if (saved != null)
                            EditorGUILayout.ObjectField("Baked Asset", saved, typeof(Mesh), false);
                    }
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }
        }

        private static void DrawStatRow(string label, int tris, Color color)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(72));
            var prev = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField($"{tris:N0} tris", EditorStyles.miniBoldLabel);
            GUI.contentColor = prev;
            EditorGUILayout.EndHorizontal();
        }

        // ─── Bake Logic ───────────────────────────────────────────────────────────

        private void ApplyDoubleSided()
        {
            if (_colliders.Count == 0) return;

            Undo.SetCurrentGroupName("Bake Double-Sided Collider");
            int g = Undo.GetCurrentGroup();

            try
            {
                for (int i = 0; i < _colliders.Count; i++)
                {
                    var  col = _colliders[i];
                    Mesh src = GetSource(col);
                    if (src == null) continue;

                    EditorUtility.DisplayProgressBar(
                        "Baking", col.gameObject.name, (float)i / _colliders.Count);

                    Mesh origRef = col.sharedMesh;
                    if (!_bakedOrig.ContainsKey(col)) _bakedOrig[col] = origRef;

                    Mesh ds = MeshAlgorithms.BuildDoubleSidedMesh(src);
                    ds.name = src.name + MeshColliderToolsSettings.SuffixDoubleSided;

                    // src is a DeepCopy returned by GetSource — safe to destroy now
                    // that ds holds all vertex/triangle data via array assignment.
                    Object.DestroyImmediate(src);

                    string path = MeshAssetUtils.SaveMeshAsset(ds, origRef);
                    if (!string.IsNullOrEmpty(path))
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                        var loaded = MeshAssetUtils.LoadMesh(path);
                        if (loaded != null) ds = loaded;
                        _assetPaths[col] = path;
                    }

                    Undo.RecordObject(col, "Set Double-Sided Mesh");
                    col.sharedMesh = ds;

                    if (MeshColliderToolsSettings.AutoAddComponent)
                        SyncComponent(col, origRef, path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Undo.CollapseUndoOperations(g);
            AssetDatabase.SaveAssets();
            RefreshColliders();
        }

        private void RevertAll()
        {
            Undo.SetCurrentGroupName("Revert Double-Sided Colliders");
            int g = Undo.GetCurrentGroup();

            foreach (var col in _colliders)
            {
                if (col == null) continue;
                var ds = col.GetComponent<DoubleSidedMeshCollider>();

                _bakedOrig.TryGetValue(col, out Mesh orig);
                if (orig == null && ds != null) orig = ds.originalMeshRef;
                if (orig == null) orig = col.GetComponent<MeshFilter>()?.sharedMesh;

                _assetPaths.TryGetValue(col, out string path);
                if (string.IsNullOrEmpty(path) && ds != null) path = ds.savedAssetPath;

                if (orig != null) { Undo.RecordObject(col, "Revert"); col.sharedMesh = orig; }
                else Debug.LogWarning(
                    $"[InnerCollider] Could not find original mesh for '{col.gameObject.name}'.", col);

                if (!string.IsNullOrEmpty(path))
                    MeshAssetUtils.DeleteAsset(path);
                else if (col.sharedMesh != null)
                    MeshAssetUtils.DeleteAssetByName(col.sharedMesh.name);

                ClearComponent(ds);
            }

            Undo.CollapseUndoOperations(g);
            AssetDatabase.SaveAssets();
            _bakedOrig.Clear();
            _assetPaths.Clear();
            RefreshColliders();
        }

        private void RevertSingle(MeshCollider col, DoubleSidedMeshCollider ds)
        {
            Undo.SetCurrentGroupName("Revert Collider");
            int g = Undo.GetCurrentGroup();

            Mesh orig = ds?.originalMeshRef ?? col.GetComponent<MeshFilter>()?.sharedMesh;
            if (orig != null) { Undo.RecordObject(col, "Revert"); col.sharedMesh = orig; }

            string path = ds?.savedAssetPath ?? "";
            if (!string.IsNullOrEmpty(path))
                MeshAssetUtils.DeleteAsset(path);
            else if (col.sharedMesh != null)
                MeshAssetUtils.DeleteAssetByName(col.sharedMesh.name);

            ClearComponent(ds);

            Undo.CollapseUndoOperations(g);
            _bakedOrig.Remove(col);
            _assetPaths.Remove(col);
            EditorSceneManager.MarkSceneDirty(col.gameObject.scene);
            RefreshColliders();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static Mesh GetSource(MeshCollider col)
        {
            if (col == null) return null;
            var  mf  = col.GetComponent<MeshFilter>();
            var  src = mf != null && mf.sharedMesh != null ? mf.sharedMesh : col.sharedMesh;
            return src != null ? MeshAlgorithms.DeepCopy(src) : null;
        }

        private static void SyncComponent(MeshCollider col, Mesh originalRef, string assetPath)
        {
            var  comp  = col.GetComponent<DoubleSidedMeshCollider>();
            bool isNew = comp == null;
            if (isNew)
            {
                comp = col.gameObject.AddComponent<DoubleSidedMeshCollider>();
                Undo.RegisterCreatedObjectUndo(comp, "Add DoubleSidedMeshCollider");
            }
            else Undo.RecordObject(comp, "Sync DS State");

            comp.isDoubleSided   = true;
            comp.originalMeshRef = originalRef;
            comp.savedAssetPath  = assetPath;
            EditorUtility.SetDirty(comp);
        }

        private static void ClearComponent(DoubleSidedMeshCollider ds)
        {
            if (ds == null) return;
            Undo.RecordObject(ds, "Revert DS State");
            ds.isDoubleSided   = false;
            ds.originalMeshRef = null;
            ds.savedAssetPath  = "";
            EditorUtility.SetDirty(ds);
        }
    }
}