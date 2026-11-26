using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class GhostPreview : MonoBehaviour
{
    public Transform Root => transform;
    private Transform visual;
    private readonly List<Material> materials = new();
    private readonly List<Mesh> baked = new();
    internal bool IsBakedMesh(Mesh mesh) => baked.Contains(mesh);
    private readonly List<Color> colors = new();
    private float opacity;
    public static GhostPreview Create(Rigidbody source, Shader shader, float opacity)
    {
        if (!shader)
            return null;
        var g = new GameObject("Placement Ghost").AddComponent<GhostPreview>();
        g.opacity = opacity;
        g.transform.SetPositionAndRotation(source.position, source.rotation);
        g.visual = new GameObject("Visual").transform;
        g.visual.SetParent(g.transform, false);
        foreach (var r in source.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            Mesh mesh;
            if (r is SkinnedMeshRenderer skin)
            {
                mesh = new Mesh();
                skin.BakeMesh(mesh);
                g.baked.Add(mesh);
            }
            else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var f))
                mesh = f.sharedMesh;
            else
                continue;
            if (!mesh)
                continue;
            var node = new GameObject(r.name);
            node.layer = r.gameObject.layer;
            node.transform.SetParent(g.visual, false);
            node.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            node.transform.localScale = r.transform.lossyScale;
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = node.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var originals = r.sharedMaterials;
            var copies = new Material[originals.Length];
            for (int i = 0; i < copies.Length; i++)
            {
                var original = originals[i];
                var m = new Material(shader);
                Color c = Color.white;
                if (original)
                {
                    if (original.HasProperty("_BaseColor"))
                        c = original.GetColor("_BaseColor");
                    else if (original.HasProperty("_Color"))
                        c = original.color;
                    string tex = original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    if (original.HasProperty(tex) && m.HasProperty("_BaseMap"))
                    {
                        m.SetTexture("_BaseMap", original.GetTexture(tex));
                        m.SetTextureScale("_BaseMap", original.GetTextureScale(tex));
                        m.SetTextureOffset("_BaseMap", original.GetTextureOffset(tex));
                    }
                }
                bool cutout = original &&
                              (original.IsKeywordEnabled("_ALPHATEST_ON") ||
                               (original.HasProperty("_AlphaClip") && original.GetFloat("_AlphaClip") > .5f));
                m.SetFloat("_Cutoff",
                           cutout && original.HasProperty("_Cutoff") ? original.GetFloat("_Cutoff") : 0f);
                m.SetFloat("_SourceAlpha", c.a);
                m.SetShaderPassEnabled("ShadowCaster", false);
                copies[i] = m;
                g.materials.Add(m);
                g.colors.Add(c);
            }
            mr.sharedMaterials = copies;
        }
        g.SetAppearance(Color.white, 0);
        return g;
    }
    public void SetAppearance(Color tint, float shake)
    {
        visual.localPosition = Vector3.right * shake;
        for (int i = 0; i < materials.Count; i++)
        {
            var c = colors[i] * tint;
            c.a = opacity * tint.a;
            materials[i].SetColor("_BaseColor", c);
        }
    }
    private void OnDestroy()
    {
        foreach (var m in materials)
            if (m)
                Destroy(m);
        foreach (var m in baked)
            if (m)
                Destroy(m);
    }
}