using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Library of decal materials the Decal Placer can paint with.
///
/// A plain top-level ScriptableObject on purpose: the prop palette's type is
/// nested inside its editor window, so renaming or moving that window would
/// orphan the saved asset. This one has nothing to lose track of.
/// </summary>
// Created from Tools > Room Building > Palette creator, not from Assets > Create:
// that route drops the asset wherever the Project window is pointing, and these
// are meant to live in one folder.
public class DecalPalette : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string name = "Decal";
        public Material material;

        [Tooltip("Footprint on the surface, in world units. Stored per decal, " +
                 "because a crack and a warning sign are not the same shape.")]
        public Vector2 size = new Vector2(10f, 10f);

        [Tooltip("How far the projector reaches into the surface. Too shallow " +
                 "and the decal drops out on uneven walls.")]
        public float projectionDepth = 4f;
    }

    public List<Entry> entries = new List<Entry>();

    public Entry Get(int index)
    {
        if (entries == null || entries.Count == 0) return null;

        index = Mathf.Clamp(index, 0, entries.Count - 1);
        return entries[index];
    }
}
