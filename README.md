# InnerCollider
**Double-sided mesh collision baking for Unity — editor tool + runtime component.**

Unity's `MeshCollider` only detects contact on the outward-facing side of a mesh. For vent tunnels, hollow pipes, concave walkable surfaces, or any geometry where a body can be *inside* the mesh, that means either manually placing proxy colliders by hand or shipping invisible geometry just for physics. InnerCollider eliminates both workarounds.

---

## How It Works

InnerCollider generates a second copy of each triangle in the source mesh with its winding order reversed and normals flipped, then merges the two into a single baked `.asset` file assigned back to the `MeshCollider`. The result is a mesh that registers physics contact from both sides — one bake, no manual work.

The baked asset is written to disk and referenced by a `DoubleSidedMeshCollider` component, so state survives domain reloads and scene serialization. Original meshes are never modified.

---

## Features

- **One-click bake** — select any GameObject(s) with a `MeshCollider`, open `Tools → InnerCollider` (`Ctrl+Shift+M`), bake. Works on multi-selection.
- **Child traversal** — optionally include children in a single pass. Useful for compound objects or prefabs with nested geometry.
- **Full Undo support** — bake and revert are both registered as a single collapsed Undo group. Ctrl+Z works as expected.
- **Persistent state** — the `DoubleSidedMeshCollider` component stores the original mesh reference and baked asset path. Bake once; the collider survives reloads and re-serialization.
- **Runtime bake mode** — `applyOnStart` flag builds the double-sided mesh in memory at `Awake()` for spawned objects. No disk write; mesh lives only for that instance's lifetime.
- **Scene-view gizmos** — selected objects with an active bake render a cyan wireframe bounds overlay and a "Double-Sided" label. Toggleable in Settings.
- **Triangle budget display** — the tool window shows original, current, and projected triangle counts per collider, colour-coded against configurable warn/error thresholds.
- **Configurable save folder** — baked assets can be written to any project folder. Falls back to beside the source mesh, then `Assets/GeneratedColliders`.
- **Safe cleanup** — revert deletes the baked asset by stored path. If the path is stale, it searches only the configured MCT folder by exact mesh name — never a project-wide delete.

---

## Performance

### The actual cost
Doubling triangle count is the direct trade-off. A 2,000-tri collider becomes 4,000 tris. Unity's PhysX evaluates collider complexity at load time and during broadphase / narrowphase queries at runtime, so triangle count is the number that matters most.

### What InnerCollider does to keep that cost minimal

**Bake happens once, at edit time.** Mesh generation runs in the editor (`MeshAlgorithms.BuildDoubleSidedMesh`), not at runtime. The result is a serialized `.asset`. In the player, PhysX loads the pre-built mesh exactly as it would any other `MeshCollider` — there is no generation pass at runtime.

**No per-frame allocation.** The editor window caches all triangle counts and collider stats in a `Dictionary<MeshCollider, RowStats>` populated during `RefreshColliders()`. `OnGUI` reads from that cache and allocates nothing per draw call.

**Deep copy before mutation.** `GetSource()` calls `MeshAlgorithms.DeepCopy()` before passing the mesh into `BuildDoubleSidedMesh`. The source asset is never modified. The copy is discarded immediately after the baked asset is imported — one temporary allocation per bake, nothing retained.

**Array-level construction.** Vertex, normal, UV, and triangle arrays are sized once and filled with index-based writes. No `List<T>` resizing, no LINQ.

### Compared to the alternative
The standard manual alternative is placing multiple primitive colliders (boxes, capsules) to approximate interior surfaces. That can achieve a lower triangle count, but requires significant hand-tuning per mesh, breaks whenever source geometry changes, and adds component overhead for each primitive. InnerCollider trades a predictable 2× triangle cost for zero manual maintenance.

---

## Installation

1. Copy the `MeshColliderTools` folder into your project's `Assets` directory.
2. Unity will compile the scripts automatically.
3. Open the tool from `Tools → InnerCollider` or press `Ctrl+Shift+M`.

No package dependencies. No third-party libraries. Requires Unity 2021.3 or later.

---

## Usage

### Editor bake
1. Select one or more GameObjects with `MeshCollider` components.
2. Open **Tools → InnerCollider**.
3. Toggle **Children** if you want the pass to include child objects.
4. Click **Bake Double-Sided**.
5. To undo: click **Revert All** or press `Ctrl+Z`.

### Per-collider control
Each row in the collider list has an individual **Revert** button. Expand the foldout to inspect original / current / projected triangle counts, vertex count, and asset references.

### Runtime-spawned objects
Add `DoubleSidedMeshCollider` to the prefab and enable `Apply On Start`. The double-sided mesh is built in memory on `Awake()`. No asset is written; the mesh is discarded when the object is destroyed.

---

## Settings

Accessible from the **Settings** tab in the tool window. All values are stored in `EditorPrefs` and are per-machine.

| Setting | Default | Description |
|---|---|---|
| Save Folder | `Assets/GeneratedColliders` | Where baked `.asset` files are written |
| Show Gizmos | `true` | Scene-view bounds overlay for baked colliders |
| Auto-Add Component | `true` | Attach `DoubleSidedMeshCollider` automatically on bake |
| Double-Sided Suffix | `_doubleSided` | Appended to the baked asset filename |
| Warn Triangle Count | `10,000` | Amber threshold in the triangle budget display |
| Error Triangle Count | `50,000` | Red threshold in the triangle budget display |

---

## File Overview

| File | Responsibility |
|---|---|
| `MeshColliderToolsWindow.cs` | Editor window — UI, bake orchestration, Undo |
| `MeshAlgorithms.cs` | Stateless mesh generation. No editor dependency — safe at runtime |
| `DoubleSidedMeshCollider.cs` | MonoBehaviour — persists bake state, handles runtime mode |
| `MeshAssetUtils.cs` | All `AssetDatabase` I/O. Single point of entry for reads, writes, deletes |
| `MeshColliderToolsSettings.cs` | `EditorPrefs`-backed settings. Typed properties, no magic strings at callsites |
| `MeshColliderToolsGizmos.cs` | `SceneView` overlay. Registered once via `[InitializeOnLoad]` |

---

## Roadmap

### v1.2 — Primitive Auto-Fill *(planned)*
For geometry that doesn't need exact surface fidelity, v1.2 will automatically decompose the interior volume into fitted primitive colliders — boxes, spheres, or capsules — without manual placement. This removes the 2× triangle cost of a mesh collider entirely, replacing it with a small number of zero-triangle primitives and the same one-click workflow. Target use cases: crates, rooms, pipes with uniform cross-sections, and any interior space where approximate collision is acceptable.

---

## License

MIT. See `LICENSE` for details.
