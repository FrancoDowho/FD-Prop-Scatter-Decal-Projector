using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Paints URP decal projectors onto surfaces.
///
/// Decals here are DecalProjector components rather than meshes -- the project
/// runs URP with the decal renderer feature already enabled, and the two
/// projectors already in the project (the boss telegraphs) set the convention
/// this follows: the projector's +Z points INTO the surface, and its pivot is
/// pushed half the projection depth forward so the box starts at the surface.
/// </summary>
public class DecalPlacerTool : EditorWindow
{
    private DecalPalette _palette;
    private int _selectedIndex;
    private bool _placementActive;

    private bool _alignToNormal = true;
    private float _surfaceOffset = 0.05f;

    private bool _randomRoll = true;
    private float _rollMin = 0f;
    private float _rollMax = 360f;

    private bool _randomScale = true;
    private float _scaleMin = 0.8f;
    private float _scaleMax = 1.25f;

    private bool _scatterMode = true;
    private float _scatterSpacing = 8f;

    private int _roll;
    private bool _hasHit;
    private Vector3 _hitPoint;
    private Vector3 _hitNormal;
    private Transform _hitTransform;
    private bool _dragging;
    private Vector3 _lastPaintPoint;
    private GameObject _lastPlaced;

    [MenuItem("Tools/FD Painters/Decal Projector")]
    private static void Open()
    {
        GetWindow<DecalPlacerTool>("Decal Placer").Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Decal Placer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _palette = (DecalPalette)EditorGUILayout.ObjectField("Palette", _palette, typeof(DecalPalette), false);

        if (_palette == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a Decal Palette. Create one with Assets > Create > Room Building > Decal Palette.",
                MessageType.Info);
            return;
        }

        DrawPaletteSelection();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);

        _alignToNormal = EditorGUILayout.ToggleLeft(
            new GUIContent("Align to surface normal",
                "Off: the decal keeps world orientation, which only makes sense on flat ground."),
            _alignToNormal);

        _surfaceOffset = EditorGUILayout.FloatField(
            new GUIContent("Surface offset",
                "Pulls the projector slightly off the surface so it is not sitting exactly on the boundary."),
            _surfaceOffset);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Variation", EditorStyles.boldLabel);

        _randomRoll = EditorGUILayout.ToggleLeft("Random rotation", _randomRoll);
        if (_randomRoll)
        {
            EditorGUI.indentLevel++;
            _rollMin = EditorGUILayout.FloatField("Min", _rollMin);
            _rollMax = EditorGUILayout.FloatField("Max", _rollMax);
            EditorGUI.indentLevel--;
        }

        _randomScale = EditorGUILayout.ToggleLeft("Random scale", _randomScale);
        if (_randomScale)
        {
            EditorGUI.indentLevel++;
            _scaleMin = EditorGUILayout.FloatField("Min", _scaleMin);
            _scaleMax = EditorGUILayout.FloatField("Max", _scaleMax);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scatter", EditorStyles.boldLabel);

        _scatterMode = EditorGUILayout.ToggleLeft(
            new GUIContent("Paint while dragging",
                "Off: one decal per click."),
            _scatterMode);

        using (new EditorGUI.DisabledScope(!_scatterMode))
        {
            EditorGUI.indentLevel++;
            _scatterSpacing = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                new GUIContent("Spacing", "Minimum distance between decals while dragging."),
                _scatterSpacing));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(_palette.Get(_selectedIndex) == null))
        {
            GUI.backgroundColor = _placementActive ? new Color(1f, 0.6f, 0.4f) : Color.white;
            if (GUILayout.Button(_placementActive ? "Painting -- click a surface (Esc to stop)" : "Start painting",
                    GUILayout.Height(30)))
            {
                _placementActive = !_placementActive;
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Hover a surface to preview, click to place, drag to paint a trail.\n" +
            "Number keys 1..9 switch decal, R rerolls the preview.\n\n" +
            "Works inside prefab mode, so decals land in the room prefab itself.",
            MessageType.None);
    }

    private void DrawPaletteSelection()
    {
        if (_palette.entries == null || _palette.entries.Count == 0)
        {
            EditorGUILayout.HelpBox("The palette has no entries yet.", MessageType.Warning);
            return;
        }

        var names = new string[_palette.entries.Count];
        for (int i = 0; i < names.Length; i++)
        {
            DecalPalette.Entry entry = _palette.entries[i];
            string label = entry == null || string.IsNullOrEmpty(entry.name) ? "(unnamed)" : entry.name;
            names[i] = (i + 1) + "  " + label;
        }

        _selectedIndex = EditorGUILayout.Popup("Decal", Mathf.Clamp(_selectedIndex, 0, names.Length - 1), names);

        DecalPalette.Entry selected = _palette.Get(_selectedIndex);
        if (selected != null && selected.material == null)
            EditorGUILayout.HelpBox("That entry has no material assigned.", MessageType.Warning);
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        DecalPalette.Entry entry = _palette != null ? _palette.Get(_selectedIndex) : null;
        if (!_placementActive || entry == null || entry.material == null)
            return;

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        Event e = Event.current;

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                _placementActive = false;
                Repaint();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.R)
            {
                _roll++;
                sceneView.Repaint();
                e.Use();
            }

            int index = KeyToIndex(e.keyCode);
            if (index >= 0 && _palette.entries != null && index < _palette.entries.Count)
            {
                _selectedIndex = index;
                Repaint();
                sceneView.Repaint();
                e.Use();
            }
        }

        UpdateHit(e);
        DrawPreview(entry);

        if (_hasHit)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Place(entry);
                _dragging = true;
                _lastPaintPoint = _hitPoint;
                e.Use();
            }
            else if (_scatterMode && _dragging && e.type == EventType.MouseDrag && e.button == 0 && !e.alt)
            {
                if (Vector3.Distance(_hitPoint, _lastPaintPoint) >= _scatterSpacing)
                {
                    Place(entry);
                    _lastPaintPoint = _hitPoint;
                }
                e.Use();
            }
        }

        if (e.type == EventType.MouseUp && e.button == 0)
            _dragging = false;

        sceneView.Repaint();
    }

    private void UpdateHit(Event e)
    {
        _hasHit = false;
        _hitTransform = null;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        PhysicsScene physicsScene = stage != null
            ? stage.scene.GetPhysicsScene()
            : Physics.defaultPhysicsScene;

        // Triggers are ignored: a room is full of invisible volumes sitting right
        // where you want to paint, and a decal projected onto one would hang in
        // mid-air with nothing to show for it.
        if (!physicsScene.Raycast(ray.origin, ray.direction, out RaycastHit hit, 10000f,
                                  Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        _hasHit = true;
        _hitPoint = hit.point;
        _hitNormal = hit.normal;
        _hitTransform = hit.transform;
    }

    /// <summary>
    /// Pose and size for the decal about to be placed. Derived from a counter
    /// rather than Random, so what the preview shows is exactly what gets
    /// placed instead of rerolling on click.
    /// </summary>
    private void GetPlacement(DecalPalette.Entry entry, out Vector3 position, out Quaternion rotation, out Vector3 size)
    {
        Vector3 normal = _alignToNormal ? _hitNormal : Vector3.up;

        // The projector fires along its +Z, so it has to look into the surface.
        rotation = LookRotationSafe(-normal, Vector3.up);

        uint hash = Hash((uint)_roll * 2654435761u + (uint)_selectedIndex);

        if (_randomRoll)
        {
            float angle = Mathf.Lerp(_rollMin, _rollMax, Unit(hash));
            rotation = Quaternion.AngleAxis(angle, -normal) * rotation;
        }

        float scale = _randomScale ? Mathf.Lerp(_scaleMin, _scaleMax, Unit(hash ^ 0x9E3779B9u)) : 1f;

        size = new Vector3(
            Mathf.Max(0.001f, entry.size.x * scale),
            Mathf.Max(0.001f, entry.size.y * scale),
            Mathf.Max(0.001f, entry.projectionDepth));

        position = _hitPoint + normal * _surfaceOffset;
    }

    private void DrawPreview(DecalPalette.Entry entry)
    {
        if (!_hasHit)
            return;

        GetPlacement(entry, out Vector3 position, out Quaternion rotation, out Vector3 size);

        Vector3 right = rotation * Vector3.right * (size.x * 0.5f);
        Vector3 up = rotation * Vector3.up * (size.y * 0.5f);

        Handles.color = new Color(0.4f, 1f, 0.9f, 0.9f);
        Handles.DrawSolidRectangleWithOutline(
            new[]
            {
                position - right - up,
                position + right - up,
                position + right + up,
                position - right + up
            },
            new Color(0.4f, 1f, 0.9f, 0.12f),
            new Color(0.4f, 1f, 0.9f, 0.9f));

        // Which way the projection goes, and how deep.
        Vector3 into = rotation * Vector3.forward;
        Handles.DrawDottedLine(position, position + into * size.z, 3f);
        Handles.ArrowHandleCap(0, position, rotation, Mathf.Min(size.z, 6f), EventType.Repaint);

        if (_scatterMode && _dragging)
        {
            Handles.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Handles.DrawWireDisc(_lastPaintPoint, _hitNormal, _scatterSpacing);
        }
    }

    private void Place(DecalPalette.Entry entry)
    {
        GetPlacement(entry, out Vector3 position, out Quaternion rotation, out Vector3 size);

        var go = new GameObject("Decal_" + (string.IsNullOrEmpty(entry.name) ? "Unnamed" : entry.name));
        Undo.RegisterCreatedObjectUndo(go, "Place Decal");

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        Transform parent = ResolveParent(stage);
        if (parent != null)
            Undo.SetTransformParent(go.transform, parent, "Place Decal");
        else
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());

        go.transform.SetPositionAndRotation(position, rotation);

        var projector = Undo.AddComponent<DecalProjector>(go);
        projector.material = entry.material;
        projector.size = size;

        // Half the depth forward, so the box starts at the surface and projects
        // inward rather than straddling it. Matches the projectors already in
        // the project.
        projector.pivot = new Vector3(0f, 0f, size.z * 0.5f);

        _lastPlaced = go;
        _roll++;
    }

    /// <summary>
    /// Decals go under the surface they were painted on, so moving a wall takes
    /// its grime with it. Falls back to the prefab root.
    /// </summary>
    private Transform ResolveParent(PrefabStage stage)
    {
        if (_hitTransform != null && (stage == null || _hitTransform.gameObject.scene == stage.scene))
        {
            // Never nest a decal inside another decal.
            if (_lastPlaced == null || _hitTransform != _lastPlaced.transform)
                return _hitTransform;
        }

        return stage != null ? stage.prefabContentsRoot.transform : null;
    }

    private static int KeyToIndex(KeyCode key)
    {
        if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9) return key - KeyCode.Alpha1;
        if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad9) return key - KeyCode.Keypad1;
        return -1;
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    private static float Unit(uint x)
    {
        return (Hash(x) & 0x00FFFFFFu) / 16777216f;
    }

    private static Quaternion LookRotationSafe(Vector3 forward, Vector3 up)
    {
        if (forward.sqrMagnitude < 0.000001f)
            return Quaternion.identity;

        if (Vector3.Cross(forward, up).sqrMagnitude < 0.000001f)
            up = Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)) > 0.9f
                ? Vector3.forward
                : Vector3.up;

        return Quaternion.LookRotation(forward, up);
    }
}
