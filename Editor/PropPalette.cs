using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Library of prefabs the Prop Scatter can paint with.
///
/// A plain top-level ScriptableObject, and that is the whole point: the previous
/// palette type lived nested inside the editor window, and Unity never writes a
/// script reference for a nested type in an editor assembly. The asset saved
/// fine, then failed to deserialise on the next domain reload -- which read as
/// "the tool stopped working" rather than "the asset is broken".
/// </summary>
public class PropPalette : ScriptableObject
{
    public List<GameObject> prefabs = new List<GameObject>();

    public int Count => prefabs != null ? prefabs.Count : 0;

    public GameObject Get(int index)
    {
        if (prefabs == null || prefabs.Count == 0) return null;

        index = Mathf.Clamp(index, 0, prefabs.Count - 1);
        return prefabs[index];
    }

    /// <summary>
    /// Next filled slot at or after <paramref name="from"/>, walking in
    /// <paramref name="step"/> direction and wrapping. Returns -1 when every
    /// slot is empty. Empty slots are skipped rather than selected, because
    /// landing on one used to switch the whole tool off in silence.
    /// </summary>
    public int NextFilled(int from, int step)
    {
        if (prefabs == null || prefabs.Count == 0) return -1;

        int count = prefabs.Count;
        for (int i = 1; i <= count; i++)
        {
            int index = ((from + step * i) % count + count) % count;
            if (prefabs[index] != null) return index;
        }

        return prefabs[Mathf.Clamp(from, 0, count - 1)] != null ? from : -1;
    }
}
