#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public class PropScatterTool : EditorWindow
{
    [MenuItem("Tools/FD Painters/Prop Scatter")]
    public static void Open()
    {
        var w = GetWindow<PropScatterTool>();
        w.titleContent = new GUIContent("Prop Scatter");
        w.Show();
    }

    // ---------- Settings ----------
    PropPalette paletteAsset;

    int selectedIndex = 0;
    bool isEnabled = true;

    LayerMask surfaceMask = ~0;
    float surfaceOffset = 0.0f;

    bool alignToNormal = true;
    Vector3 eulerOffset = Vector3.zero;

    bool randomYaw = true;
    float yawMin = 0f;
    float yawMax = 360f;
    float yawPreview = 0f;

    bool randomScale = false;
    float scaleMin = 0.9f;
    float scaleMax = 1.1f;
    float scaleMul = 1f;

    bool parentToSelection = true;

    bool hasHit;
    RaycastHit lastHit;

    public enum SnapMode { Pivot, MeshBounds, RendererBounds, ColliderBounds }

    [Header("Snap")]
    public SnapMode snapMode = SnapMode.RendererBounds;
    public bool snapPreview = true;
    public float snapExtraOffset = 0.001f;

    // ---------- Preview Random (Seed) ----------
    [Header("Preview Random")]
    public int previewSeed = 12345;
    [SerializeField] int _previewRoll = 0; 
    [SerializeField] float _previewYaw;
    [SerializeField] float _previewScale = 1f;

    void OnEnable()
    {
        SceneView.duringSceneGui += DuringSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGUI;
    }

    List<GameObject> PrefabsList => paletteAsset != null ? paletteAsset.prefabs : null;

    GameObject GetSelectedPrefab()
    {
        var list = PrefabsList;
        if (list == null || list.Count == 0) return null;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, list.Count - 1);
        return list[selectedIndex];
    }

    /// <summary>
    /// Moves the selection to the next filled slot. Empty slots are skipped:
    /// landing on one used to disable the whole tool without saying so.
    /// </summary>
    void StepSelection(int step)
    {
        if (paletteAsset == null) return;

        int next = paletteAsset.NextFilled(selectedIndex, step);
        if (next >= 0) selectedIndex = next;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Prop Scatter", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Palette first, everything else after: with no palette there is nothing
        // for the rest of the window to act on.
        paletteAsset = (PropPalette)EditorGUILayout.ObjectField("Palette", paletteAsset, typeof(PropPalette), false);

        if (paletteAsset == null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Assign a Prop Palette to begin.\n\n" +
                "Create one with Tools > Room Building > Palette creator > New Prop Palette.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope())
        {
            isEnabled = EditorGUILayout.ToggleLeft("Enable Tool", isEnabled, GUILayout.Width(110));
            parentToSelection = EditorGUILayout.ToggleLeft("Parent To Selection", parentToSelection);
        }

        EditorGUILayout.Space(6);
        DrawPrefabListUI();

        EditorGUILayout.Space(10);
        DrawSelectionUI();

        EditorGUILayout.Space(10);
        DrawPlacementUI();

        EditorGUILayout.Space(10);
        DrawPreviewRandomUI();

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Scene controls:\n" +
            "- Move mouse over surface (raycast) to preview\n" +
            "- LMB: Place (places EXACTLY what you preview)\n" +
            "- Number keys 1..9: select prefab index\n" +
            "- + / - : previous and next prefab (skips empty slots)\n" +
            "- R: Reroll preview random\n" +
            "\nThe Scene view needs focus for the keys to reach the tool: click in it first.",
            MessageType.None
        );
    }

    void DrawPrefabListUI()
    {
        var list = PrefabsList;
        if (list == null) return;

        EditorGUILayout.LabelField($"Prefabs ({list.Count})", EditorStyles.boldLabel);

        int removeAt = -1;

        for (int i = 0; i < list.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label((i + 1).ToString(), GUILayout.Width(24));
                list[i] = (GameObject)EditorGUILayout.ObjectField(list[i], typeof(GameObject), false);

                if (GUILayout.Button("X", GUILayout.Width(22)))
                    removeAt = i;
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ Add"))
                list.Add(null);

            if (GUILayout.Button("Clear"))
                list.Clear();
        }

        if (removeAt >= 0)
            list.RemoveAt(removeAt);

        if (GUI.changed)
            EditorUtility.SetDirty(paletteAsset);
    }

    void DrawSelectionUI()
    {
        var list = PrefabsList;
        if (list == null) return;

        EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);

        int count = list.Count;
        if (count <= 0)
        {
            EditorGUILayout.HelpBox("Add at least 1 prefab to the palette.", MessageType.Warning);
            return;
        }

        int oneBased = Mathf.Clamp(selectedIndex + 1, 1, count);
        int newOneBased = EditorGUILayout.IntField("Prefab #", oneBased);

        newOneBased = Mathf.Clamp(newOneBased, 1, count);
        selectedIndex = newOneBased - 1;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("< Prev")) selectedIndex = Mathf.Max(0, selectedIndex - 1);
            if (GUILayout.Button("Next >")) selectedIndex = Mathf.Min(count - 1, selectedIndex + 1);
        }

        var sel = GetSelectedPrefab();
        EditorGUILayout.ObjectField("Selected Prefab", sel, typeof(GameObject), false);
    }

    void DrawPlacementUI()
    {
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);

        surfaceMask = LayerMaskField("Surface Mask", surfaceMask);
        surfaceOffset = EditorGUILayout.FloatField("Surface Offset", surfaceOffset);

        alignToNormal = EditorGUILayout.Toggle("Align To Normal", alignToNormal);
        eulerOffset = EditorGUILayout.Vector3Field("Euler Offset", eulerOffset);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Random", EditorStyles.boldLabel);

        randomYaw = EditorGUILayout.Toggle("Random Yaw", randomYaw);
        if (randomYaw)
        {
            yawMin = EditorGUILayout.FloatField("Yaw Min", yawMin);
            yawMax = EditorGUILayout.FloatField("Yaw Max", yawMax);
        }
        else
        {
            yawPreview = EditorGUILayout.Slider("Yaw", yawPreview, 0f, 360f);
        }

        randomScale = EditorGUILayout.Toggle("Random Scale", randomScale);
        if (randomScale)
        {
            scaleMin = EditorGUILayout.FloatField("Scale Min", scaleMin);
            scaleMax = EditorGUILayout.FloatField("Scale Max", scaleMax);
        }

        scaleMul = EditorGUILayout.FloatField("Scale Multiplier", scaleMul);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Snap", EditorStyles.boldLabel);

        snapMode = (SnapMode)EditorGUILayout.EnumPopup("Snap Mode", snapMode);
        snapPreview = EditorGUILayout.Toggle("Snap Preview", snapPreview);
        snapExtraOffset = EditorGUILayout.FloatField("Snap Extra Offset", snapExtraOffset);
    }

    void DrawPreviewRandomUI()
    {
        EditorGUILayout.LabelField("Preview Random", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            previewSeed = EditorGUILayout.IntField("Seed", previewSeed);

            if (GUILayout.Button("Reroll", GUILayout.Width(70)))
                RerollPreview();

            if (GUILayout.Button("Random Seed", GUILayout.Width(100)))
            {
                previewSeed = unchecked(Environment.TickCount * 1103515245 + 12345);
                RerollPreview();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Preview Yaw", GUILayout.Width(90));
            EditorGUILayout.LabelField(_previewYaw.ToString("0.###"));

            EditorGUILayout.LabelField("Preview Scale", GUILayout.Width(100));
            EditorGUILayout.LabelField(_previewScale.ToString("0.###"));
        }
    }

    void RerollPreview()
    {
        _previewRoll = unchecked(_previewRoll + (int)0x9E3779B9u);
        Repaint();
        SceneView.RepaintAll();
    }

    void DuringSceneGUI(SceneView sv)
    {
        if (!isEnabled) return;
        if (paletteAsset == null) return;

        Event e = Event.current;

        // Shortcuts are handled before the prefab is resolved on purpose. The old
        // order bailed out when the selected slot was empty, which killed the very
        // keys you needed to move off that empty slot.
        if (e.type == EventType.KeyDown)
        {
            int idx = KeyToIndex(e.keyCode);
            if (idx >= 0)
            {
                var list = PrefabsList;
                if (list != null && idx < list.Count)
                {
                    selectedIndex = idx;
                    Repaint();
                    sv.Repaint();
                    e.Use();
                }
            }

            int step = KeyToStep(e.keyCode);
            if (step != 0)
            {
                StepSelection(step);
                Repaint();
                sv.Repaint();
                e.Use();
            }

            if (e.keyCode == KeyCode.R)
            {
                RerollPreview();
                e.Use();
            }
        }

        var prefab = GetSelectedPrefab();
        if (prefab == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        hasHit = RaycastActiveStage(ray, out lastHit);

        if (hasHit)
        {
            Vector3 hitN = lastHit.normal.normalized;
            Vector3 planePoint = lastHit.point + hitN * surfaceOffset;

            ComputePreviewRandom(prefab, out float yawForPreview, out float scaleForPreview);
            _previewYaw = yawForPreview;
            _previewScale = scaleForPreview;

            Quaternion rot = GetPlacementRotation(hitN, yawForPreview);
            rot *= Quaternion.Euler(eulerOffset);

            Vector3 previewPos = planePoint;

            if (snapPreview && snapMode != SnapMode.Pivot)
            {
                previewPos = ApplyPreviewSnap(prefab, planePoint, rot, scaleForPreview, hitN)
                           + hitN * snapExtraOffset;
            }
            else
            {
                previewPos += hitN * snapExtraOffset;
            }

            DrawPrefabPreview(prefab, previewPos, rot, scaleForPreview);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                PlacePrefab(prefab, planePoint, hitN, yawForPreview, scaleForPreview);
                RerollPreview();         
                e.Use();
            }
        }

        if (hasHit) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
    }

    /// <summary>
    /// Raycasts whichever scene the user is actually looking at.
    ///
    /// Prefab mode opens the asset in its own preview scene with its own physics
    /// world. A plain Physics.Raycast only ever queries the default one, so the
    /// tool silently found nothing there -- which is exactly where rooms get
    /// built, since they are prefabs.
    /// </summary>
    bool RaycastActiveStage(Ray ray, out RaycastHit hit)
    {
        PhysicsScene physicsScene = GetActivePhysicsScene();

        return physicsScene.Raycast(ray.origin, ray.direction, out hit, 9999f,
                                    surfaceMask, QueryTriggerInteraction.Ignore);
    }

    static PhysicsScene GetActivePhysicsScene()
    {
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        return stage != null ? stage.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
    }

    void ComputePreviewRandom(GameObject prefab, out float yaw, out float uniformScale)
    {
        yaw = randomYaw ? 0f : yawPreview;
        uniformScale = Mathf.Max(0.0001f, scaleMul);

        if (!randomYaw && !randomScale) return;

        int prefabId = prefab != null ? prefab.GetInstanceID() : 0;

        uint h = HashU32((uint)CombineSeed(previewSeed, _previewRoll, selectedIndex, prefabId));

        if (randomYaw)
        {
            float t = Hash01(h ^ 0xA3C59AC3u);
            yaw = Mathf.Lerp(yawMin, yawMax, t);
        }

        if (randomScale)
        {
            float t = Hash01(h ^ 0x3C6EF372u);
            float s = Mathf.Lerp(scaleMin, scaleMax, t);
            uniformScale *= Mathf.Max(0.0001f, s);
        }
    }

    static int CombineSeed(int a, int b, int c, int d)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + a;
            h = h * 31 + b;
            h = h * 31 + c;
            h = h * 31 + d;
            return h;
        }
    }

    Quaternion GetPlacementRotation(Vector3 normal, float yaw)
    {
        if (!alignToNormal)
            return Quaternion.Euler(0f, yaw, 0f);

        Quaternion baseRot = Quaternion.FromToRotation(Vector3.up, normal.normalized);
        Quaternion yawRot = Quaternion.AngleAxis(yaw, normal.normalized);
        return yawRot * baseRot;
    }

    void PlacePrefab(GameObject prefab, Vector3 planePoint, Vector3 normal, float yawToUse, float uniformScaleToUse)
    {
        Quaternion rot = GetPlacementRotation(normal, yawToUse);
        rot *= Quaternion.Euler(eulerOffset);

        // Instantiate into the scene being edited. Without the scene argument the
        // prop lands in the active scene, so in prefab mode it would end up
        // outside the prefab entirely -- placed, visible, and lost on save.
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        Scene targetScene = stage != null ? stage.scene : SceneManager.GetActiveScene();

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, targetScene);
        Undo.RegisterCreatedObjectUndo(go, "Place Prop");

        Transform parent = ResolveParent(stage);
        if (parent != null)
            go.transform.SetParent(parent, true);

        go.transform.SetPositionAndRotation(planePoint, rot);

       
        go.transform.localScale = go.transform.localScale * Mathf.Max(0.0001f, uniformScaleToUse);

        if (snapMode != SnapMode.Pivot)
        {
            ApplySnapToSurface(go, planePoint, normal.normalized);
            go.transform.position += normal.normalized * snapExtraOffset;
        }
        else
        {
            go.transform.position += normal.normalized * snapExtraOffset;
        }

        _lastPlaced = go;
        Selection.activeGameObject = go;
    }

    // The prop placed by the previous click, so the next one does not get
    // parented into it.
    GameObject _lastPlaced;

    /// <summary>
    /// Where the new prop should hang.
    ///
    /// "Parent To Selection" used to nest props into each other: placing one
    /// selects it, so the next click parented into the previous prop and you
    /// ended up with a staircase instead of a flat list of siblings. When the
    /// selection is just the prop we placed a moment ago, its parent is used
    /// instead.
    /// </summary>
    Transform ResolveParent(PrefabStage stage)
    {
        if (parentToSelection)
        {
            Transform selected = Selection.activeTransform;

            if (selected != null && _lastPlaced != null && selected == _lastPlaced.transform)
                selected = selected.parent;

            // Ignore a selection that lives outside whatever is open, or the prop
            // would be yanked out of the prefab.
            if (selected != null && BelongsToStage(selected, stage))
                return selected;
        }

        // Falling back to the prefab root keeps the prop inside the asset.
        return stage != null ? stage.prefabContentsRoot.transform : null;
    }

    static bool BelongsToStage(Transform candidate, PrefabStage stage)
    {
        if (stage == null)
            return PrefabStageUtility.GetPrefabStage(candidate.gameObject) == null;

        return candidate.gameObject.scene == stage.scene;
    }

    void DrawPrefabPreview(GameObject prefab, Vector3 pos, Quaternion rot, float uniformScale)
    {
        if (Event.current.type != EventType.Repaint) return;

        EnsurePreviewMat();
        if (_previewMat == null) return;

        var mfs = prefab.GetComponentsInChildren<MeshFilter>(true);
        if (mfs == null || mfs.Length == 0) return;

        _previewMat.SetPass(0);
        _previewMat.color = new Color(0f, 1f, 1f, 0.18f);

        Vector3 rootScale = prefab.transform.localScale * Mathf.Max(0.0001f, uniformScale);
        Matrix4x4 rootPlacement = Matrix4x4.TRS(pos, rot, rootScale);

        Transform root = prefab.transform;

        foreach (var mf in mfs)
        {
            if (mf == null || mf.sharedMesh == null) continue;

            Matrix4x4 rootToChild = GetRootToChildMatrix(mf.transform, root);
            Matrix4x4 final = rootPlacement * rootToChild;
            Graphics.DrawMeshNow(mf.sharedMesh, final);
        }
    }

    // helpers
    static uint HashU32(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352d;
        x ^= x >> 15;
        x *= 0x846ca68b;
        x ^= x >> 16;
        return x;
    }

    static float Hash01(uint x)
    {
        return (HashU32(x) & 0x00FFFFFFu) / 16777216f;
    }
    static Matrix4x4 LocalTRS(Transform t)
    {
        return Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
    }

    static Matrix4x4 GetRootToChildMatrix(Transform child, Transform root)
    {
        if (child == null || root == null) return Matrix4x4.identity;
        if (child == root) return Matrix4x4.identity;

        Transform cur = child;
        Matrix4x4 m = Matrix4x4.identity;

        while (cur != null && cur != root)
        {
            m = LocalTRS(cur) * m;
            cur = cur.parent;
        }

        return m;
    }

    int KeyToIndex(KeyCode k)
    {
        if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return (int)(k - KeyCode.Alpha1);
        if (k >= KeyCode.Keypad1 && k <= KeyCode.Keypad9) return (int)(k - KeyCode.Keypad1);
        return -1;
    }

    /// <summary>
    /// Step through the palette. Both the main row and the numpad, and Equals
    /// too, since '+' on the main row is Shift+Equals and pressing it without
    /// shift is the more natural thing to try.
    /// </summary>
    static int KeyToStep(KeyCode k)
    {
        if (k == KeyCode.Plus || k == KeyCode.KeypadPlus || k == KeyCode.Equals) return 1;
        if (k == KeyCode.Minus || k == KeyCode.KeypadMinus || k == KeyCode.Underscore) return -1;
        return 0;
    }

    static LayerMask LayerMaskField(string label, LayerMask selected)
    {
        var layers = new List<string>();
        var layerNumbers = new List<int>();

        for (int i = 0; i < 32; i++)
        {
            string layerName = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(layerName))
            {
                layers.Add(layerName);
                layerNumbers.Add(i);
            }
        }

        int maskWithoutEmpty = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if (((1 << layerNumbers[i]) & selected.value) != 0)
                maskWithoutEmpty |= (1 << i);
        }

        maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers.ToArray());

        int mask = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if ((maskWithoutEmpty & (1 << i)) != 0)
                mask |= (1 << layerNumbers[i]);
        }

        selected.value = mask;
        return selected;
    }

    static Material _previewMat;

    static void EnsurePreviewMat()
    {
        if (_previewMat != null) return;

        Shader sh = Shader.Find("Hidden/Internal-Colored");
        _previewMat = new Material(sh);
        _previewMat.hideFlags = HideFlags.HideAndDontSave;

        _previewMat.SetInt("_ZWrite", 0);
        _previewMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _previewMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _previewMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    }

    void ApplySnapToSurface(GameObject go, Vector3 planePoint, Vector3 n)
    {
        float minProj = float.PositiveInfinity;

        if (snapMode == SnapMode.MeshBounds)
        {
            var mfs = go.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in mfs)
            {
                if (mf == null || mf.sharedMesh == null) continue;

                Bounds b = mf.sharedMesh.bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;

                for (int xi = -1; xi <= 1; xi += 2)
                    for (int yi = -1; yi <= 1; yi += 2)
                        for (int zi = -1; zi <= 1; zi += 2)
                        {
                            Vector3 localCorner = c + Vector3.Scale(e, new Vector3(xi, yi, zi));
                            Vector3 worldCorner = mf.transform.TransformPoint(localCorner);

                            float proj = Vector3.Dot(n, worldCorner);
                            if (proj < minProj) minProj = proj;
                        }
            }
        }
        else if (snapMode == SnapMode.ColliderBounds)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
            {
                if (c == null || !c.enabled) continue;
                MinProjectionFromBounds(c.bounds, n, ref minProj);
            }
        }
        else // RendererBounds
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (r == null || !r.enabled) continue;
                MinProjectionFromBounds(r.bounds, n, ref minProj);
            }
        }

        if (float.IsInfinity(minProj)) return;

        float target = Vector3.Dot(n, planePoint);
        float delta = target - minProj;

        go.transform.position += n * delta;
    }

    static void MinProjectionFromBounds(Bounds b, Vector3 n, ref float minProj)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;

        for (int xi = -1; xi <= 1; xi += 2)
            for (int yi = -1; yi <= 1; yi += 2)
                for (int zi = -1; zi <= 1; zi += 2)
                {
                    Vector3 p = c + Vector3.Scale(e, new Vector3(xi, yi, zi));
                    float proj = Vector3.Dot(n, p);
                    if (proj < minProj) minProj = proj;
                }
    }

    Vector3 ApplyPreviewSnap(GameObject prefab, Vector3 pos, Quaternion rot, float uniformScale, Vector3 n)
    {
        n = n.normalized;

        var mfs = prefab.GetComponentsInChildren<MeshFilter>(true);
        if (mfs == null || mfs.Length == 0) return pos;

        Vector3 rootScale = prefab.transform.localScale * Mathf.Max(0.0001f, uniformScale);
        Matrix4x4 rootPlacement = Matrix4x4.TRS(pos, rot, rootScale);

        Transform root = prefab.transform;

        float minProj = float.PositiveInfinity;

        foreach (var mf in mfs)
        {
            if (mf == null || mf.sharedMesh == null) continue;

            Matrix4x4 rootToChild = GetRootToChildMatrix(mf.transform, root);
            Matrix4x4 M = rootPlacement * rootToChild;

            Bounds b = mf.sharedMesh.bounds;
            Vector3 c = b.center;
            Vector3 e = b.extents;

            for (int xi = -1; xi <= 1; xi += 2)
                for (int yi = -1; yi <= 1; yi += 2)
                    for (int zi = -1; zi <= 1; zi += 2)
                    {
                        Vector3 localCorner = c + Vector3.Scale(e, new Vector3(xi, yi, zi));
                        Vector3 worldCorner = M.MultiplyPoint3x4(localCorner);

                        float proj = Vector3.Dot(n, worldCorner);
                        if (proj < minProj) minProj = proj;
                    }
        }

        if (float.IsInfinity(minProj)) return pos;

        float target = Vector3.Dot(n, pos);
        float delta = target - minProj;
        return pos + n * delta;
    }
}

#endif
