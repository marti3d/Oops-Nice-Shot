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



    private Vector3[] originalArcVertices, animatedArcVertices;
    private Vector3[] originalRingVertices, animatedRingVertices;
    private float rotateWavePhase;

    private Vector3[] originalArrowVertices, animatedArrowVertices;
    private int waveFrame = -1;
    private float arrowWavePhase;

    private float drawnBrushWobble = -1f, drawnBrushWidthVariation = -1f;
    private Vector3[][] arrowPaths, ringPaths;

    private readonly float[] planeSigns = { 1, 1, 1 };
    private Vector3 PlanePoint(int i, float u, float v) =>
        ghost.Root.position
        + EffectiveGizmoSize * (PlaneDirection((i + 1) % 3) * (planeOffset + u * PlaneSize) +
                                PlaneDirection((i + 2) % 3) * (planeOffset + v * PlaneSize));


    private float planeWavePhase;
    private Vector3[] originalTileFill, animatedTileFill, originalTileBorder, animatedTileBorder;

    private static readonly Vector2 TileCenter = new(.5f, .5f);
    private Vector2[] planeOutline;
    private float drawnPlaneRoundness, drawnPlaneRoughness;

    private Mesh BuildPlaneBorder()
    {
        const int samples = 80;
        planeOutline = new Vector2[samples];
        var fill = new Vector3[samples + 1];
        fill[samples] = TileCenter;
        var fillTriangles = new List<int>();
        var border = new List<Vector3>();
        var edges = new[] { new List<int>(), new List<int>() };
        for (int i = 0; i < samples; i++)
        {
            float angle = 2f * Mathf.PI * i / samples;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 abs = new(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
            float flat = .5f - planeCornerRoundness;
            float radius = .5f / Mathf.Max(abs.x, abs.y);
            if (radius * Mathf.Min(abs.x, abs.y) > flat)
            {
                float projected = flat * (abs.x + abs.y);
                radius = projected + Mathf.Sqrt(Mathf.Max(0f, projected * projected -
                         2f * flat * flat + planeCornerRoundness * planeCornerRoundness));
            }
            float roughness = planeBrushRoughness *
                              (.6f * Mathf.Sin(11f * angle) + .4f * Mathf.Sin(17f * angle + .7f));
            planeOutline[i] = TileCenter + direction * (radius + roughness);
            fill[i] = planeOutline[i];
        }

        for (int i = 0; i < samples; i++)
        {
            int next = (i + 1) % samples;
            AddTileTriangle(fillTriangles, samples, i, next);
            for (int state = 0; state < 2; state++)
            {
                float width = Mathf.Clamp01(2f * planeBorder * (state == 0 ? 1f : 1.65f));
                int start = border.Count;
                border.Add(planeOutline[i]);
                border.Add(planeOutline[next]);
                border.Add(Vector2.Lerp(planeOutline[i], TileCenter, width));
                border.Add(Vector2.Lerp(planeOutline[next], TileCenter, width));
                List<int> triangles = edges[state];
                AddTileTriangle(triangles, start, start + 1, start + 2);
                AddTileTriangle(triangles, start + 1, start + 3, start + 2);
            }
        }

        planeMesh.Clear();
        planeMesh.vertices = fill;
        planeMesh.SetTriangles(fillTriangles, 0);
        planeMesh.MarkDynamic();
        planeMesh.RecalculateBounds();
        originalTileFill = fill;
        animatedTileFill = new Vector3[fill.Length];

        var mesh = new Mesh { name = "Painted Plane Border" };
        mesh.SetVertices(border);
        mesh.subMeshCount = edges.Length;
        for (int i = 0; i < edges.Length; i++)
            mesh.SetTriangles(edges[i], i);
        mesh.MarkDynamic();
        mesh.RecalculateBounds();
        originalTileBorder = border.ToArray();
        animatedTileBorder = new Vector3[originalTileBorder.Length];
        drawnPlaneRoundness = planeCornerRoundness;
        drawnPlaneRoughness = planeBrushRoughness;
        return mesh;
    }

    private static void AddTileTriangle(List<int> triangles, int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
        triangles.Add(a);
        triangles.Add(c);
        triangles.Add(b);
    }

    private Color HandleAxisColor(int i) => i == 0 ? xColor : i == 1 ? yColor : zColor;

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
        if (rotationTool && showRotate)
            UpdateRotationMesh();
        else if (!rotationTool && showMove)
        {
            UpdateArrowMesh();
            UpdatePlaneMesh();
        }
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
            Color c = HandleAxisColor(i % 3);
            if (i == drag)
                c = activeColor;
            else if (i == hover)
            {
                if (i < 6)
                    c = Color.Lerp(c, hoverColor, brushHoverTint);
                else
                {
                    float alpha = c.a;
                    c = Color.Lerp(c, Color.white, .12f);
                    c.a = alpha;
                }
            }
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
                bool highlighted = hover == i || drag == i;
                Color edgeColor = c;
                edgeColor.a = Mathf.Clamp01(c.a * 1.5f) * planeEdgeOpacity;
                planeBorderBlock.SetColor("_BaseColor", edgeColor);
                Graphics.DrawMesh(planeBorderMesh, matrix, planeBorderMaterial, 0, cam, highlighted ? 1 : 0,
                                  planeBorderBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                c.a = Mathf.Clamp01(c.a * (highlighted ? 1.35f : 1f)) * planeFillOpacity;
                materials[i].SetColor("_BaseColor", c);
            }
            else
            {
                matrix = StrokeMatrix(i);
                mesh = i < 3 ? arrowMesh : (fullRingWhileDragging && drag == i ? fullRingMesh : ringMesh);
            }
            Graphics.DrawMesh(mesh, matrix, materials[i], 0, cam, 0, null,
                              UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
    private Matrix4x4 StrokeMatrix(int handle)
    {
        int axisIndex = handle % 3;
        Quaternion orientation = AxisRotation(axisIndex);
        if (handle < 3)
        {
            Vector3 axisDirection = Direction(axisIndex);
            Vector3 view = cam.orthographic ? -cam.transform.forward
                                            : cam.transform.position - ghost.Root.position;
            Vector3 facing = Vector3.ProjectOnPlane(view, axisDirection);
            if (facing.sqrMagnitude > 1e-6f)
                orientation = Quaternion.LookRotation(axisDirection, facing);
        }
        else
        {
            orientation *= Quaternion.AngleAxis(ArcStart(axisIndex) * Mathf.Rad2Deg, Vector3.forward);
        }
        float size = EffectiveGizmoSize * visualScale[handle] * (handle < 3 ? arrowLength : 1f);
        return Matrix4x4.TRS(ghost.Root.position, orientation, Vector3.one * size);
    }

    private bool PlaneHit(int i, Vector2 mouse)
    {
        Vector3 u = PlaneDirection((i + 1) % 3), v = PlaneDirection((i + 2) % 3), p = PlanePoint(i, 0, 0);
        Ray ray = cam.ScreenPointToRay(mouse);
        if (!new Plane(Vector3.Cross(u, v), p).Raycast(ray, out float t))
            return false;
        Vector3 d = ray.GetPoint(t) - p;
        float size = EffectiveGizmoSize * PlaneSize;
        Vector2 point = new(Vector3.Dot(d, u) / size, Vector3.Dot(d, v) / size);
        bool inside = false;
        Vector2 previous = AnimateTilePoint(planeOutline[planeOutline.Length - 1]);
        foreach (Vector2 outlinePoint in planeOutline)
        {
            Vector2 current = AnimateTilePoint(outlinePoint);
            if ((current.y > point.y) != (previous.y > point.y) &&
                point.x < (previous.x - current.x) * (point.y - current.y) /
                          (previous.y - current.y) + current.x)
                inside = !inside;
            previous = current;
        }
        return inside;
    }
    private int Pick(Vector2 mouse)
    {
        if (!ghost)
            return -1;
        AdvanceWaves();
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
            Matrix4x4 matrix = StrokeMatrix(i);
            Vector3[][] paths = i < 3 ? arrowPaths : ringPaths;
            float hitRadius = i < 3 ? arrowHitRadiusPixels : ringHitRadiusPixels;
            foreach (Vector3[] points in paths)
            {
                Vector3 a = cam.WorldToScreenPoint(StrokeWorldPoint(i, matrix, points[0]));
                for (int j = 1; j < points.Length; j++)
                {
                    Vector3 b = cam.WorldToScreenPoint(StrokeWorldPoint(i, matrix, points[j]));
                    float distance = StrokeDistance(mouse, a, b) / hitRadius;
                    if (distance < score)
                    {
                        score = distance;
                        best = i;
                    }
                    a = b;
                }
            }
        }
        return best;
    }
    private Vector3 StrokeWorldPoint(int handle, Matrix4x4 matrix, Vector3 point)
    {
        return matrix.MultiplyPoint3x4(handle < 3 ? BendArrowPoint(point) : BendRotationPoint(point));
    }

    private void AdvanceWaves()
    {
        if (waveFrame == Time.frameCount)
            return;

        waveFrame = Time.frameCount;
        if (animateArrows)
            arrowWavePhase = Mathf.Repeat(arrowWavePhase + Time.deltaTime * arrowWaveSpeed, 1f);
        if (animateRotationArrows)
            rotateWavePhase = Mathf.Repeat(rotateWavePhase + Time.deltaTime * rotateWaveSpeed, 1f);
        if (animatePlaneHandles)
            planeWavePhase = Mathf.Repeat(planeWavePhase + Time.deltaTime * planeWaveSpeed, 1f);
    }

    private Vector2 AnimateTilePoint(Vector2 point)
    {
        if (!animatePlaneHandles || planeWaveAmplitude <= 0f)
            return point;

        Vector2 offset = point - TileCenter;
        float angle = Mathf.Atan2(offset.y, offset.x);
        float wave = Mathf.Sin(3f * angle - 2f * Mathf.PI * planeWavePhase);
        return TileCenter + offset * (1f + planeWaveAmplitude * wave);
    }

    private void UpdatePlaneMesh()
    {
        UpdateTileVertices(planeMesh, originalTileFill, animatedTileFill);
        UpdateTileVertices(planeBorderMesh, originalTileBorder, animatedTileBorder);
    }

    private void UpdateTileVertices(Mesh mesh, Vector3[] original, Vector3[] animated)
    {
        for (int i = 0; i < original.Length; i++)
            animated[i] = AnimateTilePoint(original[i]);

        mesh.SetVertices(animated);
        mesh.RecalculateBounds();
    }

    private Vector3 BendArrowPoint(Vector3 point)
    {
        float t = Mathf.Clamp01(point.z);
        // Keep the root and tip on the movement axis.
        float envelope = 4f * t * (1f - t);
        float wave = animateArrows
            ? arrowWaveAmplitude * Mathf.Sin(2f * Mathf.PI * (t * arrowWaveCount - arrowWavePhase))
            : 0f;
        point.x += envelope * (arrowCurvature + wave);
        return point;
    }

    private void UpdateArrowMesh()
    {
        AdvanceWaves();
        for (int i = 0; i < originalArrowVertices.Length; i++)
            animatedArrowVertices[i] = BendArrowPoint(originalArrowVertices[i]);

        arrowMesh.SetVertices(animatedArrowVertices);
        arrowMesh.RecalculateBounds();
        arrowMesh.RecalculateNormals();
    }

    private Vector3 BendRotationPoint(Vector3 point)
    {
        float angle = Mathf.Atan2(point.y, point.x);
        // Whole wave counts keep the deformation continuous across the full-ring seam.
        float wave = animateRotationArrows
            ? rotateWaveAmplitude * Mathf.Sin(angle * rotateWaveCount - 2f * Mathf.PI * rotateWavePhase)
            : 0f;
        float scale = 1f + rotateCurvature * Mathf.Cos(2f * angle) + wave;
        point.x *= scale;
        point.y *= scale;
        return point;
    }

    private void UpdateRotationMesh()
    {
        AdvanceWaves();
        bool fullRing = fullRingWhileDragging && drag >= 3 && drag < 6;
        UpdateRotationStroke(ringMesh, originalArcVertices, animatedArcVertices);
        if (fullRing)
            UpdateRotationStroke(fullRingMesh, originalRingVertices, animatedRingVertices);
    }

    private void UpdateRotationStroke(Mesh mesh, Vector3[] original, Vector3[] animated)
    {
        for (int i = 0; i < original.Length; i++)
            animated[i] = BendRotationPoint(original[i]);

        mesh.SetVertices(animated);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
    }

    private static float StrokeDistance(Vector2 mouse, Vector3 a, Vector3 b)
    {
        if (a.z <= 0f || b.z <= 0f)
            return float.PositiveInfinity;
        Vector2 segment = (Vector2)b - (Vector2)a;
        float lengthSquared = segment.sqrMagnitude;
        float t = lengthSquared > .001f
            ? Mathf.Clamp01(Vector2.Dot(mouse - (Vector2)a, segment) / lengthSquared)
            : 0f;
        return Vector2.Distance(mouse, (Vector2)a + segment * t);
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
            label += "  " + (drag >= 6 ? planeApplied.magnitude.ToString("F2") + " m"
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
        if (planeBorderMesh && (!Mathf.Approximately(drawnPlaneBorder, planeBorder) ||
                                !Mathf.Approximately(drawnPlaneRoundness, planeCornerRoundness) ||
                                !Mathf.Approximately(drawnPlaneRoughness, planeBrushRoughness)))
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
        bool brushChanged = !Mathf.Approximately(brushWobble, drawnBrushWobble) ||
                            !Mathf.Approximately(brushWidthVariation, drawnBrushWidthVariation);
        if (!arrowMesh || brushChanged || ThicknessChanged(shaft, drawnShaft) || ThicknessChanged(head, drawnHead) ||
            !Mathf.Approximately(arrowHeadLength, drawnHeadLength))
        {
            if (arrowMesh)
                Destroy(arrowMesh);
            arrowPaths = BuildArrowPaths(head);
            arrowMesh = BuildBrushMesh("Painted Gizmo Arrow", arrowPaths, Vector3.up, shaft, false);
            arrowMesh.MarkDynamic();
            originalArrowVertices = arrowMesh.vertices;
            animatedArrowVertices = new Vector3[originalArrowVertices.Length];
            drawnShaft = shaft;
            drawnHead = head;
            drawnHeadLength = arrowHeadLength;
        }
        if (!ringMesh || brushChanged || ThicknessChanged(ring, drawnRing) || !Mathf.Approximately(ringRadius, drawnRadius) ||
            ringSegments != drawnSegments || !Mathf.Approximately(arcDegrees, drawnArc))
        {
            if (ringMesh)
                Destroy(ringMesh);
            ringPaths = BuildRingPaths(arcDegrees);
            ringMesh = BuildBrushMesh("Painted Gizmo Arc", ringPaths, Vector3.forward, ring * .6f,
                                      arcDegrees >= 360f);
            if (fullRingMesh)
                Destroy(fullRingMesh);
            fullRingMesh = BuildBrushMesh("Painted Gizmo Ring", BuildRingPaths(360f),
                                          Vector3.forward, ring * .6f, true);
            ringMesh.MarkDynamic();
            originalArcVertices = ringMesh.vertices;
            animatedArcVertices = new Vector3[originalArcVertices.Length];
            fullRingMesh.MarkDynamic();
            originalRingVertices = fullRingMesh.vertices;
            animatedRingVertices = new Vector3[originalRingVertices.Length];
            drawnRing = ring;
            drawnRadius = ringRadius;
            drawnSegments = ringSegments;
            drawnArc = arcDegrees;
        }
        drawnBrushWobble = brushWobble;
        drawnBrushWidthVariation = brushWidthVariation;
    }
    private static bool ThicknessChanged(float a,
                                         float b) => Mathf.Abs(a - b) > Mathf.Max(.00001f,
                                                                                  Mathf.Abs(b) * .025f);
    private Vector3[][] BuildArrowPaths(float headWidth)
    {
        var shaft = new Vector3[33];
        for (int i = 0; i < shaft.Length; i++)
        {
            float t = (float)i / (shaft.Length - 1);
            float wave = (Mathf.Sin(t * 4f * Mathf.PI) + .28f * Mathf.Sin(t * 7f * Mathf.PI))
                         * Mathf.Sin(t * Mathf.PI);
            shaft[i] = new Vector3(brushWobble * wave, 0f, Mathf.Lerp(.04f, 1f, t));
        }
        shaft[shaft.Length - 1] = Vector3.forward;
        var paths = new List<Vector3[]> { shaft };
        AddBrushTip(paths, shaft[shaft.Length - 1], -Vector3.forward, Vector3.right, arrowHeadLength, headWidth);
        return paths.ToArray();
    }

    private Vector3[][] BuildRingPaths(float degrees)
    {
        int segments = Mathf.Max(24, ringSegments);
        var arc = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = degrees * Mathf.Deg2Rad * i / segments;
            Vector3 radial = new(Mathf.Cos(a), Mathf.Sin(a), 0f);
            float wave = .55f * Mathf.Sin(3f * a) + .25f * Mathf.Sin(7f * a + .4f);
            arc[i] = radial * (ringRadius * (1f + brushWobble * wave));
        }
        if (degrees >= 360f)
        {
            arc[segments] = arc[0];
            return new[] { arc };
        }
        float length = ringRadius * Mathf.Min(.19f, degrees * Mathf.Deg2Rad * .35f);
        var paths = new List<Vector3[]> { arc };
        Vector3 endBack = (arc[segments - 1] - arc[segments]).normalized;
        Vector3 startBack = (arc[1] - arc[0]).normalized;
        AddBrushTip(paths, arc[segments], endBack, Vector3.Cross(Vector3.forward, endBack),
                    length, length * .48f);
        AddBrushTip(paths, arc[0], startBack, Vector3.Cross(Vector3.forward, startBack),
                    length, length * .48f);
        return paths.ToArray();
    }

    private static void AddBrushTip(List<Vector3[]> paths, Vector3 tip, Vector3 back, Vector3 wing,
                                    float length, float width)
    {
        paths.Add(CurvedStroke(tip + back * length + wing * width,
                               tip + back * (length * .33f) + wing * (width * .68f), tip));
        paths.Add(CurvedStroke(tip + back * (length * .82f) - wing * (width * 1.12f),
                               tip + back * (length * .22f) - wing * (width * .52f), tip));
    }

    private static Vector3[] CurvedStroke(Vector3 start, Vector3 control, Vector3 end)
    {
        var points = new Vector3[13];
        for (int i = 0; i < points.Length; i++)
        {
            float t = (float)i / (points.Length - 1);
            points[i] = Vector3.Lerp(Vector3.Lerp(start, control, t), Vector3.Lerp(control, end, t), t);
        }
        return points;
    }

    private Mesh BuildBrushMesh(string meshName, Vector3[][] paths, Vector3 normal, float radius, bool closed)
    {
        const int sides = 8;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (Vector3[] points in paths)
        {
            int offset = vertices.Count;
            int last = points.Length - 1;
            for (int i = 0; i <= last; i++)
            {
                int previous = i == 0 ? (closed ? last - 1 : 0) : i - 1;
                int next = i == last ? (closed ? 1 : last) : i + 1;
                Vector3 side = Vector3.Cross(normal, points[next] - points[previous]).normalized;
                float phase = 2f * Mathf.PI * i / last;
                float variation = .66f * Mathf.Sin(phase * 2f + .6f) + .34f * Mathf.Sin(phase * 5f);
                float taper = closed ? 1f : Mathf.SmoothStep(.36f, 1f, Mathf.Min(i, last - i));
                float width = radius * (1f + brushWidthVariation * variation) * taper;
                for (int j = 0; j < sides; j++)
                {
                    float a = 2f * Mathf.PI * j / sides;
                    vertices.Add(points[i] + width * (side * Mathf.Cos(a) + normal * Mathf.Sin(a)));
                }
                if (i == last)
                    continue;
                for (int j = 0; j < sides; j++)
                {
                    int a = offset + i * sides + j;
                    int b = offset + i * sides + (j + 1) % sides;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(a + sides);
                    triangles.Add(b);
                    triangles.Add(b + sides);
                    triangles.Add(a + sides);
                }
            }
            if (!closed)
            {
                int startCap = vertices.Count;
                vertices.Add(points[0]);
                int endCap = vertices.Count;
                vertices.Add(points[last]);
                for (int j = 0; j < sides; j++)
                {
                    int next = (j + 1) % sides;
                    triangles.Add(startCap);
                    triangles.Add(offset + next);
                    triangles.Add(offset + j);
                    triangles.Add(endCap);
                    triangles.Add(offset + last * sides + j);
                    triangles.Add(offset + last * sides + next);
                }
            }
        }
        var mesh = new Mesh { name = meshName };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
