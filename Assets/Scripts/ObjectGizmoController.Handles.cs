using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class ObjectGizmoController
{
    private bool Visible(int i) => rotationTool ? showRotate && i >= 3 && i < 6
                                                : showMove && (i < 3 || i >= 6);
    private Vector3 PlaneDirection(int i)
    {
        Vector3 d = Direction(i);
        if (!facePlanesTowardsCamera || drag >= 0)
            return d * (drag >= 0 ? planeSigns[i] : 1f);
        Vector3 view =
            cam.orthographic ? -cam.transform.forward : cam.transform.position - ghost.Root.position;
        return Vector3.Dot(d, view) < 0 ? -d : d;
    }
    private readonly float[] planeSigns = { 1, 1, 1 };
    private Vector3 PlanePoint(int i, float u, float v) =>
        ghost.Root.position
        + EffectiveGizmoSize * (PlaneDirection((i + 1) % 3) * (planeOffset + u * PlaneSize) +
                                PlaneDirection((i + 2) % 3) * (planeOffset + v * PlaneSize));
    private Mesh BuildPlaneBorder()
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        void Quad(float x, float y, float w, float h)
        {
            int n = v.Count;
            v.Add(new Vector3(x, y, 0));
            v.Add(new Vector3(x + w, y, 0));
            v.Add(new Vector3(x, y + h, 0));
            v.Add(new Vector3(x + w, y + h, 0));
            t.AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3, n + 2, n + 1, n, n + 3, n + 1, n + 2 });
        }
        float b = planeBorder;
        Quad(0, 0, 1, b);
        Quad(0, 1 - b, 1, b);
        Quad(0, b, b, 1 - 2 * b);
        Quad(1 - b, b, b, 1 - 2 * b);
        var m = new Mesh { name = "GizmoPlaneBorder" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }
    private float ArcStart(int i)
    {
        Vector3 view =
            cam.orthographic ? -cam.transform.forward : cam.transform.position - ghost.Root.position;
        Vector3 p = Quaternion.Inverse(AxisRotation(i)) * view;
        return Mathf.Atan2(p.y, p.x) - arcDegrees * Mathf.Deg2Rad * .5f;
    }
    private void DrawHandlesAndOutline()
    {
        RefreshGizmoMeshes();
        if (outlineMaterial && outlines.Count > 0)
        {
            if (!outlineMask || outlineMask.shader != outlineMaterial.shader)
            {
                if (outlineMask)
                    Destroy(outlineMask);
                if (outlineOverlay)
                    Destroy(outlineOverlay);
                outlineMask = new Material(outlineMaterial) { hideFlags = HideFlags.HideAndDontSave };
                outlineOverlay = new Material(outlineMaterial) { hideFlags = HideFlags.HideAndDontSave };
            }
            outlineMask.CopyPropertiesFromMaterial(outlineMaterial);
            outlineOverlay.CopyPropertiesFromMaterial(outlineMaterial);
            outlineMask.SetFloat("_MaskMode", 1);
            outlineMask.SetFloat("_Cull", 0);
            outlineMask.SetFloat("_ColorMask", 0);
            outlineMask.SetFloat("_StencilComp", 8);
            outlineMask.SetFloat("_StencilPass", 2);
            outlineMask.renderQueue = 3998;
            outlineOverlay.SetFloat("_MaskMode", 0);
            if (outlineOverlay.HasProperty("_OutlineColor"))
                outlineOverlay.SetColor("_OutlineColor", PreviewColor);
            outlineOverlay.SetFloat("_Cull", 1);
            outlineOverlay.SetFloat("_ColorMask", 15);
            outlineOverlay.SetFloat("_StencilComp", 6);
            outlineOverlay.SetFloat("_StencilPass", 0);
            outlineOverlay.renderQueue = 3999;
            foreach (var pair in outlines)
                if (pair.Key && pair.Key.gameObject.activeInHierarchy &&
                    pair.Key.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled)
                    for (int sub = 0; sub < pair.Value.subMeshCount; sub++)
                    {
                        Graphics.DrawMesh(pair.Value, pair.Key.transform.localToWorldMatrix, outlineMask,
                                          pair.Key.gameObject.layer, cam, sub, null,
                                          UnityEngine.Rendering.ShadowCastingMode.Off, false);
                        Graphics.DrawMesh(pair.Value, pair.Key.transform.localToWorldMatrix, outlineOverlay,
                                          pair.Key.gameObject.layer, cam, sub, null,
                                          UnityEngine.Rendering.ShadowCastingMode.Off, false);
                    }
        }
        for (int i = 0; i < 9; i++)
        {
            if (!Visible(i))
                continue;
            float goal = drag >= 0 ? (drag == i ? 1 : inactiveOpacity) : hover == i ? 1 : idleOpacity;
            visualAlpha[i] =
                Mathf.MoveTowards(visualAlpha[i], goal, Time.deltaTime / Mathf.Max(.01f, fadeDuration));
            visualScale[i] = Mathf.MoveTowards(visualScale[i], hover == i && drag < 0 ? hoverScale : 1,
                                               Time.deltaTime / Mathf.Max(.01f, fadeDuration));
            Color c = i % 3 == 0 ? xColor : i % 3 == 1 ? yColor : zColor;
            if (i == drag)
                c = activeColor;
            else if (i == hover)
                c = hoverColor;
            visualColors[i] = Color.Lerp(visualColors[i], c,
                                         1 - Mathf.Exp(-Time.deltaTime / Mathf.Max(.01f, fadeDuration) * 4));
            c = visualColors[i];
            c.a *= gizmoOpacity * visualAlpha[i];
            materials[i].SetColor("_BaseColor", c);
            Matrix4x4 matrix;
            Mesh mesh;
            if (i >= 6)
            {
                int n = i % 3;
                Vector3 u = PlaneDirection((n + 1) % 3), v = PlaneDirection((n + 2) % 3);
                matrix = Matrix4x4.TRS(PlanePoint(n, 0, 0), Quaternion.LookRotation(Vector3.Cross(u, v), v),
                                       Vector3.one * EffectiveGizmoSize * PlaneSize);
                mesh = planeMesh;
                Color border = c;
                border.a = Mathf.Clamp01(c.a * 1.5f);
                // Per-handle property block avoids shared material color changes.
                planeBorderBlock.SetColor("_BaseColor", border);
                Graphics.DrawMesh(planeBorderMesh, matrix, planeBorderMaterial, 0, cam, 0, planeBorderBlock,
                                  UnityEngine.Rendering.ShadowCastingMode.Off, false);
                c.a *= .45f;
                materials[i].SetColor("_BaseColor", c);
            }
            else
            {
                float scale = EffectiveGizmoSize * (i < 3 ? arrowLength : 1) * visualScale[i];
                Quaternion q = AxisRotation(i % 3);
                if (i >= 3)
                    q *= Quaternion.AngleAxis(ArcStart(i % 3) * Mathf.Rad2Deg, Vector3.forward);
                matrix = Matrix4x4.TRS(ghost.Root.position, q, Vector3.one * scale);
                mesh = i < 3 ? arrowMesh : (fullRingWhileDragging && drag == i ? fullRingMesh : ringMesh);
            }
            Graphics.DrawMesh(mesh, matrix, materials[i], 0, cam, 0, null,
                              UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
    private Vector3 RingPoint(int i, float a) => ghost.Root.position +
                                                 AxisRotation(i) *
                                                     new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * ringRadius
                                                     * EffectiveGizmoSize * visualScale[i + 3];
    private bool PlaneHit(int i, Vector2 mouse)
    {
        Vector3 u = PlaneDirection((i + 1) % 3), v = PlaneDirection((i + 2) % 3), p = PlanePoint(i, 0, 0);
        Ray ray = cam.ScreenPointToRay(mouse);
        if (!new Plane(Vector3.Cross(u, v), p).Raycast(ray, out float t))
            return false;
        Vector3 d = ray.GetPoint(t) - p;
        float size = EffectiveGizmoSize * PlaneSize;
        float x = Vector3.Dot(d, u), y = Vector3.Dot(d, v);
        return x >= 0 && y >= 0 && x <= size && y <= size;
    }
    private int Pick(Vector2 mouse)
    {
        if (!ghost)
            return -1;
        if (!rotationTool && showMove)
            for (int i = 0; i < 3; i++)
                if (PlaneHit(i, mouse))
                    return i + 6;
        int best = -1;
        float score = 1;
        for (int i = 0; i < 6; i++)
        {
            if (!Visible(i))
                continue;
            int n = i < 3 ? 1 : ringSegments;
            float start = i < 3 ? 0 : ArcStart(i % 3), span = arcDegrees * Mathf.Deg2Rad;
            for (int j = 0; j < n; j++)
            {
                Vector3 a = cam.WorldToScreenPoint(i < 3 ? ghost.Root.position
                                                         : RingPoint(i % 3, start + span * j / n));
                Vector3 b =
                    cam.WorldToScreenPoint(i < 3 ? ghost.Root.position + Direction(i) * EffectiveGizmoSize *
                                                                             arrowLength * visualScale[i]
                                                 : RingPoint(i % 3, start + span * (j + 1) / n));
                if (a.z <= 0 || b.z <= 0)
                    continue;
                Vector2 ab = (Vector2)b - (Vector2)a;
                if (ab.sqrMagnitude < .001f)
                    continue;
                float t = Mathf.Clamp01(Vector2.Dot(mouse - (Vector2)a, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(mouse, (Vector2)a + ab * t) /
                          (i < 3 ? arrowHitRadiusPixels : ringHitRadiusPixels);
                if (d < score)
                {
                    score = d;
                    best = i;
                }
            }
        }
        return best;
    }
    private bool PlaneDragPoint(Vector2 mouse, out Vector3 point)
    {
        Ray ray = cam.ScreenPointToRay(mouse);
        point = Vector3.zero;
        if (!new Plane(axis, origin).Raycast(ray, out float t))
            return false;
        point = ray.GetPoint(t);
        return true;
    }
    private void OnGUI()
    {
        if (!showReadout || !ghost || !cam)
            return;
        Vector3 p = cam.WorldToScreenPoint(ghost.Root.position);
        if (p.z <= 0)
            return;
        string label = showToolLabel ? (rotationTool ? "ROTATE" : "MOVE") : "";
        if (showSpaceLabel)
            label += (label.Length > 0 ? " / " : "") + (localSpace ? "LOCAL" : "WORLD");
        if (drag >= 0 && (drag >= 6 || drag < 3 ? showMovementDistance : showRotationAngle))
            label += "  " + (drag >= 6  ? planeApplied.magnitude.ToString("F2") + " m"
                             : drag < 3 ? applied.ToString("+0.00;-0.00;0.00") + " m"
                                        : applied.ToString("F1") + "°");
        if (Snap && showSnapIndicator)
            label += "  SNAP";
        GUI.Label(new Rect(p.x - 100, Screen.height - p.y + 25, 260, 25), label);
    }
    private bool AxisParam(Vector2 mouse, out float s)
    {
        Ray r = cam.ScreenPointToRay(mouse);
        Vector3 w = origin - r.origin;
        float b = Vector3.Dot(axis, r.direction), den = 1 - b * b;
        s = 0;
        if (den < .001f)
            return false;
        s = (b * Vector3.Dot(r.direction, w) - Vector3.Dot(axis, w)) / den;
        return true;
    }
    private bool Radial(Vector2 mouse, out Vector3 radial)
    {
        Ray ray = cam.ScreenPointToRay(mouse);
        Plane plane = new(axis, origin);
        radial = Vector3.zero;
        if (!plane.Raycast(ray, out float t))
            return false;
        radial = ray.GetPoint(t) - origin;
        if (radial.sqrMagnitude < .000001f)
            return false;
        radial.Normalize();
        return true;
    }
    private void BeginDrag(int handle, Vector2 mouse)
    {
        for (int i = 0; i < 3; i++)
            planeSigns[i] = Vector3.Dot(PlaneDirection(i), Direction(i)) < 0 ? -1 : 1;
        origin = startPosition = ghost.Root.position;
        startRotation = ghost.Root.rotation;
        dragFrame = Frame;
        axis = dragFrame * AxisDirs[handle % 3];
        if (handle >= 6)
        {
            planeU = dragFrame * AxisDirs[(handle + 1) % 3];
            planeV = dragFrame * AxisDirs[(handle + 2) % 3];
            if (!PlaneDragPoint(mouse, out planeGrab))
                return;
            planeTarget = planeApplied = Vector3.zero;
        }
        else if (handle < 3 ? !AxisParam(mouse, out grab) : !Radial(mouse, out lastRadial))
            return;
        remainingLift = 0f;
        drag = handle;
        target = applied = angle = 0;
        if (cameraController)
        {
            savedCameraInput = cameraController.InputEnabled;
            cameraController.InputEnabled = false;
        }
    }
    private void UpdateTarget(Vector2 mouse)
    {
        if (drag >= 6)
        {
            if (!PlaneDragPoint(mouse, out Vector3 point))
                return;
            Vector3 d = point - planeGrab;
            float u = Vector3.Dot(d, planeU), v = Vector3.Dot(d, planeV);
            if (Snap)
            {
                u = Mathf.Round(u / moveSnap) * moveSnap;
                v = Mathf.Round(v / moveSnap) * moveSnap;
            }
            planeTarget = planeU * u + planeV * v;
            return;
        }
        if (drag < 3)
        {
            if (!AxisParam(mouse, out float s))
                return;
            target = s - grab;
            if (Snap)
                target = Mathf.Round(target / moveSnap) * moveSnap;
        }
        else
        {
            if (!Radial(mouse, out var r))
                return;
            angle += Vector3.SignedAngle(lastRadial, r, axis) * rotationSensitivity;
            lastRadial = r;
            target = Snap ? Mathf.Round(angle / rotationSnap) * rotationSnap : angle;
        }
    }
    private void EndDrag()
    {
        if (drag >= 0 && cameraController)
            cameraController.InputEnabled = savedCameraInput;
        drag = -1;
    }
    // Convert desired screen pixels to world units using the actual camera projection.
    private void RefreshGizmoMeshes()
    {
        if (planeBorderMesh && !Mathf.Approximately(drawnPlaneBorder, planeBorder))
        {
            Destroy(planeBorderMesh);
            planeBorderMesh = BuildPlaneBorder();
            drawnPlaneBorder = planeBorder;
        }
        float shaft = arrowShaftThickness, head = arrowHeadWidth, ring = ringThickness;
        if (adaptiveGizmoThickness && sizeMode != GizmoSizeMode.WorldSize && selected && cam &&
            cam.pixelHeight > 0)
        {
            float depth =
                Mathf.Max(cam.nearClipPlane, Vector3.Dot((ghost ? ghost.Root.position : selected.position) -
                                                             cam.transform.position,
                                                         cam.transform.forward));
            float worldPerPixel =
                cam.orthographic
                    ? 2f * cam.orthographicSize / cam.pixelHeight
                    : 2f * depth * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f) / cam.pixelHeight;
            float arrowScale = Mathf.Max(.0001f, EffectiveGizmoSize * arrowLength);
            float ringScale = Mathf.Max(.0001f, EffectiveGizmoSize);
            float cap = Mathf.Max(1f, maximumThicknessMultiplier);
            shaft = Mathf.Clamp(minimumArrowPixels * worldPerPixel / (2f * arrowScale), shaft, shaft * cap);
            head = Mathf.Clamp(minimumHeadPixels * worldPerPixel / (2f * arrowScale), head, head * cap);
            ring = Mathf.Clamp(minimumRingPixels * worldPerPixel / ringScale, ring, ring * cap);
            // Avoid a shaft wider than its arrowhead or a ring collapsing into a disk.
            shaft = Mathf.Min(shaft, Mathf.Max(arrowShaftThickness, head * .7f));
            ring = Mathf.Min(ring, Mathf.Max(ringThickness, ringRadius * .5f));
        }
        if (!arrowMesh || ThicknessChanged(shaft, drawnShaft) || ThicknessChanged(head, drawnHead) ||
            !Mathf.Approximately(arrowHeadLength, drawnHeadLength))
        {
            if (arrowMesh)
                Destroy(arrowMesh);
            arrowMesh = BuildArrowMesh(shaft, head);
            drawnShaft = shaft;
            drawnHead = head;
            drawnHeadLength = arrowHeadLength;
        }
        if (!ringMesh || ThicknessChanged(ring, drawnRing) || !Mathf.Approximately(ringRadius, drawnRadius) ||
            ringSegments != drawnSegments || !Mathf.Approximately(arcDegrees, drawnArc))
        {
            if (ringMesh)
                Destroy(ringMesh);
            ringMesh = BuildRing(ring, arcDegrees);
            if (fullRingMesh)
                Destroy(fullRingMesh);
            fullRingMesh = BuildRing(ring, 360);
            drawnRing = ring;
            drawnRadius = ringRadius;
            drawnSegments = ringSegments;
            drawnArc = arcDegrees;
        }
    }
    private static bool ThicknessChanged(float a,
                                         float b) => Mathf.Abs(a - b) > Mathf.Max(.00001f,
                                                                                  Mathf.Abs(b) * .025f);
    private Mesh BuildRing(float thickness, float degrees)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        int n = Mathf.Max(24, ringSegments), sides = 8;
        for (int i = 0; i <= n; i++)
        {
            float a = degrees * Mathf.Deg2Rad * i / n;
            Vector3 radial = new(Mathf.Cos(a), Mathf.Sin(a), 0);
            for (int j = 0; j < sides; j++)
            {
                float b = 2 * Mathf.PI * j / sides;
                v.Add(radial * (ringRadius + thickness * .5f * Mathf.Cos(b)) +
                      Vector3.forward * (thickness * .5f * Mathf.Sin(b)));
            }
        }
        for (int i = 0; i < n; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * sides + j, b = i * sides + (j + 1) % sides, c = (i + 1) * sides + j,
                    d = (i + 1) * sides + (j + 1) % sides;
                t.AddRange(new[] { a, b, c, b, d, c });
            }
        if (degrees < 359.99f)
            foreach (float a in new[] { 0f, degrees * Mathf.Deg2Rad })
            {
                Vector3 center = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * ringRadius;
                int offset = v.Count;
                const int lat = 6, lon = 12;
                for (int j = 0; j <= lat; j++)
                    for (int k = 0; k <= lon; k++)
                    {
                        float phi = Mathf.PI * j / lat, theta = 2 * Mathf.PI * k / lon;
                        v.Add(center + thickness * .5f *
                                           new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta),
                                                       Mathf.Sin(phi) * Mathf.Sin(theta), Mathf.Cos(phi)));
                    }
                for (int j = 0; j < lat; j++)
                    for (int k = 0; k < lon; k++)
                    {
                        int x = offset + j * (lon + 1) + k, y = x + lon + 1;
                        t.AddRange(new[] { x, y, x + 1, x + 1, y, y + 1 });
                    }
            }
        Mesh m = new() { name = "GizmoRing" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }
    private Mesh BuildArrowMesh(float shaftR, float headR)
    {
        const int seg = 16;
        float shaftLen = 1f - arrowHeadLength;

        Vector3[] v = new Vector3[seg * 3 + 2];
        for (int i = 0; i < seg; i++)
        {
            float ang = 2f * Mathf.PI * i / seg;
            float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang);
            v[i] = new Vector3(cx * shaftR, cy * shaftR, 0f);
            v[seg + i] = new Vector3(cx * shaftR, cy * shaftR, shaftLen);
            v[2 * seg + i] = new Vector3(cx * headR, cy * headR, shaftLen);
        }
        v[3 * seg] = new Vector3(0f, 0f, 1f);
        v[3 * seg + 1] = new Vector3(0f, 0f, shaftLen);

        List<int> t = new List<int>();
        for (int i = 0; i < seg; i++)
        {
            int j = (i + 1) % seg;
            t.Add(i);
            t.Add(seg + i);
            t.Add(seg + j);
            t.Add(i);
            t.Add(seg + j);
            t.Add(j);
            t.Add(2 * seg + i);
            t.Add(3 * seg);
            t.Add(2 * seg + j);
            t.Add(3 * seg + 1);
            t.Add(2 * seg + j);
            t.Add(2 * seg + i);
        }

        Mesh mesh = new Mesh { name = "GizmoArrow" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
