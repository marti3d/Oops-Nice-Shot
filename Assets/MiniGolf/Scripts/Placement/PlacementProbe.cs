using UnityEngine;

internal sealed class PlacementProbe
{
    private Collider[] overlaps = new Collider[16];
    private RaycastHit[] hits = new RaycastHit[16];

    public static Bounds Measure(MeshRenderer[] renderers, Vector3 fallback, Vector3 visualOffset)
    {
        Bounds bounds = new Bounds(fallback, Vector3.zero);
        bool found = false;
        foreach (var renderer in renderers)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (found)
                bounds.Encapsulate(renderer.bounds);
            else
                bounds = renderer.bounds;
            found = true;
        }
        if (found)
            bounds.center -= visualOffset;
        return bounds;
    }

    public bool FindGround(Bounds bounds, Rigidbody original, LayerMask layers, LayerMask forbidden,
                           float distance, out Vector3 start, out RaycastHit best, out bool blocked)
    {
        start = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        float lift = bounds.size.y + .05f;
        Vector3 origin = start + Vector3.up * lift;
        int count;
        while ((count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, distance + lift,
                                                layers.value | forbidden.value, QueryTriggerInteraction.Collide)) == hits.Length)
            System.Array.Resize(ref hits, hits.Length * 2);

        best = default;
        blocked = false;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.rigidbody == original || hit.collider.transform.IsChildOf(original.transform))
                continue;
            bool prohibited = (forbidden.value & (1 << hit.collider.gameObject.layer)) != 0;
            if (!prohibited && (hit.collider.isTrigger || hit.normal.y <= 0f))
                continue;
            if (hit.point.y > start.y + .05f || hit.distance > nearest ||
                (hit.distance == nearest && !prohibited))
                continue;
            nearest = hit.distance;
            best = hit;
            blocked = prohibited;
        }
        return !blocked && nearest < float.PositiveInfinity;
    }

    public bool CanPlace(Rigidbody selected, Collider[] shapes, Vector3 p, Quaternion q,
                         LayerMask obstacleLayers, float collisionClearance)
    {
        Vector3 oldP = selected.position;
        Quaternion delta = q * Quaternion.Inverse(selected.rotation);
        foreach (var c in shapes)
        {
            if (!c || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy ||
                c.attachedRigidbody != selected)
                continue;
            if (!(c is BoxCollider || c is SphereCollider || c is CapsuleCollider ||
                  (c is MeshCollider mc && mc.convex)))
                return false;
            Vector3 cp = p + delta * (c.transform.position - oldP);
            Quaternion cq = delta * c.transform.rotation;
            Bounds b = c.bounds;
            Vector3 center = p + delta * (b.center - oldP);
            float radius = b.extents.magnitude + collisionClearance;
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(center, radius, overlaps, obstacleLayers,
                                                          QueryTriggerInteraction.Ignore)) == overlaps.Length)
                System.Array.Resize(ref overlaps, overlaps.Length * 2);
            for (int i = 0; i < count; i++)
            {
                var other = overlaps[i];
                if (!other || other.attachedRigidbody == selected ||
                    Physics.GetIgnoreLayerCollision(c.gameObject.layer, other.gameObject.layer) ||
                    Physics.GetIgnoreCollision(c, other))
                    continue;
                bool overlap = Physics.ComputePenetration(c, cp, cq, other, other.transform.position,
                                                          other.transform.rotation, out _, out float depth);
                if (overlap && depth > .00001f)
                    return false;
            }
        }
        return true;
    }
}
