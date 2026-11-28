using System.Collections.Generic;
using UnityEngine;

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
    private readonly Mesh[] arcMeshes = new Mesh[3];
    private readonly float[] rotateHoverBlend = new float[3];
    private readonly float[] rotateWavePhase = new float[3];
    private readonly float[] rotateWaveTime = new float[3];
    private readonly float[] rotateWaveVariation = new float[3];

    private Vector3[] originalArrowVertices, animatedArrowVertices;
    private readonly Mesh[] arrowMeshes = new Mesh[3];
    private readonly float[] arrowHoverBlend = new float[3];
    private readonly float[] arrowWavePhase = new float[3];
    private readonly float[] arrowWaveTime = new float[3];
    private readonly float[] arrowWaveVariation = new float[3];

    private float drawnBrushWobble = -1f, drawnBrushWidthVariation = -1f;
    private Vector3[][] arrowPaths, ringPaths;

    private readonly float[] planeSigns = { 1, 1, 1 };
    private Vector3 PlanePoint(int i, float u, float v) =>
        ghost.Root.position
        + EffectiveGizmoSize * (PlaneDirection((i + 1) % 3) * (planeOffset + u * PlaneSize) +
                                PlaneDirection((i + 2) % 3) * (planeOffset + v * PlaneSize));


    private readonly Mesh[] planeMeshes = new Mesh[3];
    private readonly Mesh[] planeBorderMeshes = new Mesh[3];
    private readonly float[] planeHoverBlend = new float[3];
    private readonly float[] planeWavePhase = new float[3];
    private readonly float[] planeWaveTime = new float[3];
    private readonly float[] planeWaveVariation = new float[3];
    private Vector3[] originalTileFill, animatedTileFill, originalTileBorder, animatedTileBorder;

    private static readonly Vector2 TileCenter = new(.5f, .5f);
    private Vector2[] planeOutline;
    private float drawnPlaneRoundness, drawnPlaneRoughness;

    private void BuildPlaneMeshes()
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

        DestroyPlaneMeshes();
        var planeMesh = new Mesh { name = "Painted Plane Fill" };
        planeMesh.vertices = fill;
        planeMesh.SetTriangles(fillTriangles, 0);
        planeMesh.MarkDynamic();
        planeMesh.RecalculateBounds();
        originalTileFill = fill;
        animatedTileFill = new Vector3[fill.Length];

        var borderMesh = new Mesh { name = "Painted Plane Border" };
        borderMesh.SetVertices(border);
        borderMesh.subMeshCount = edges.Length;
        for (int i = 0; i < edges.Length; i++)
            borderMesh.SetTriangles(edges[i], i);
        borderMesh.MarkDynamic();
        borderMesh.RecalculateBounds();
        originalTileBorder = border.ToArray();
        animatedTileBorder = new Vector3[originalTileBorder.Length];
        drawnPlaneRoundness = planeCornerRoundness;
        drawnPlaneRoughness = planeBrushRoughness;
        drawnPlaneBorder = planeBorder;
        planeMeshes[0] = planeMesh;
        planeBorderMeshes[0] = borderMesh;
        for (int i = 1; i < planeMeshes.Length; i++)
        {
            planeMeshes[i] = Instantiate(planeMesh);
            planeMeshes[i].MarkDynamic();
            planeBorderMeshes[i] = Instantiate(borderMesh);
            planeBorderMeshes[i].MarkDynamic();
        }
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
            UpdateArrowMeshes();
            UpdatePlaneMeshes();
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
                mesh = planeMeshes[n];
                bool highlighted = hover == i || drag == i;
                Color edgeColor = c;
                edgeColor.a = Mathf.Clamp01(c.a * 1.5f) * planeEdgeOpacity;
                planeBorderBlock.SetColor("_BaseColor", edgeColor);
                Graphics.DrawMesh(planeBorderMeshes[n], matrix, planeBorderMaterial, 0, cam, highlighted ? 1 : 0,
                                  planeBorderBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                c.a = Mathf.Clamp01(c.a * (highlighted ? 1.35f : 1f)) * planeFillOpacity;
                materials[i].SetColor("_BaseColor", c);
            }
            else
            {
                matrix = StrokeMatrix(i);
                mesh = i < 3 ? arrowMeshes[i] : (fullRingWhileDragging && drag == i ? fullRingMesh : arcMeshes[i - 3]);
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
        Vector2 previous = AnimateTilePoint(i, planeOutline[planeOutline.Length - 1]);
        foreach (Vector2 outlinePoint in planeOutline)
        {
            Vector2 current = AnimateTilePoint(i, outlinePoint);
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
        return matrix.MultiplyPoint3x4(handle < 3 ? BendArrowPoint(handle, point) : BendRotationPoint(handle - 3, point));
    }

    private Vector2 AnimateTilePoint(int planeIndex, Vector2 point)
    {
        float blend = HoverEase(planeHoverBlend[planeIndex], planeHoverEasing);
        if (blend <= 0f || planeWaveAmplitude <= 0f)
            return point;

        Vector2 offset = point - TileCenter;
        float angle = Mathf.Atan2(offset.y, offset.x);
        float wave = Mathf.Sin(3f * angle - 2f * Mathf.PI * planeWavePhase[planeIndex]);
        float strength = planeWaveAmplitude * (1f + .45f * planeWaveVariation[planeIndex]);
        return TileCenter + offset * (1f + blend * strength * wave);
    }

    private void UpdatePlaneMeshes()
    {
        for (int planeIndex = 0; planeIndex < planeMeshes.Length; planeIndex++)
        {
            int handle = planeIndex + 6;
            bool engaged = animatePlaneHandles && (drag >= 0 ? drag == handle : hover == handle);
            float previous = planeHoverBlend[planeIndex];
            float duration = Mathf.Max(.01f, engaged ? planeHoverFadeIn : planeHoverFadeOut);
            float blend = Mathf.MoveTowards(previous, engaged ? 1f : 0f, Time.deltaTime / duration);
            planeHoverBlend[planeIndex] = blend;
            if (previous == 0f && blend == 0f)
                continue;

            if (blend > 0f)
            {
                planeWaveTime[planeIndex] += Time.deltaTime * planeWaveSpeed;
                float noise = Mathf.Clamp01(Mathf.PerlinNoise(planeWaveTime[planeIndex] * .41f,
                                                             91f + 11f * planeIndex));
                float variation = (2f * noise - 1f) * planeWaveRandomness;
                planeWaveVariation[planeIndex] = variation;
                float speed = planeWaveSpeed * (1f + .55f * variation);
                planeWavePhase[planeIndex] = Mathf.Repeat(planeWavePhase[planeIndex] + Time.deltaTime * speed, 1f);
            }

            UpdateTileVertices(planeIndex, planeMeshes[planeIndex], originalTileFill, animatedTileFill);
            UpdateTileVertices(planeIndex, planeBorderMeshes[planeIndex], originalTileBorder, animatedTileBorder);
        }
    }

    private void UpdateTileVertices(int planeIndex, Mesh mesh, Vector3[] original, Vector3[] animated)
    {
        for (int i = 0; i < original.Length; i++)
            animated[i] = AnimateTilePoint(planeIndex, original[i]);

        mesh.SetVertices(animated);
        mesh.RecalculateBounds();
    }

    private void DestroyPlaneMeshes()
    {
        for (int i = 0; i < planeMeshes.Length; i++)
        {
            if (planeMeshes[i])
                Destroy(planeMeshes[i]);
            if (planeBorderMeshes[i])
                Destroy(planeBorderMeshes[i]);
        }
    }

    private Vector3 BendArrowPoint(int handle, Vector3 point)
    {
        float t = Mathf.Clamp01(point.z);
        float envelope = 4f * t * (1f - t);
        float variation = arrowWaveVariation[handle];
        float wave = animateArrows
            ? arrowWaveAmplitude * (1f + .45f * variation) *
              Mathf.Sin(2f * Mathf.PI * (t * (arrowWaveCount + .3f * variation) - arrowWavePhase[handle]))
            : 0f;
        float brushBend = brushWobble *
            (Mathf.Sin(t * 4f * Mathf.PI) + .28f * Mathf.Sin(t * 7f * Mathf.PI)) *
            Mathf.Sin(t * Mathf.PI);
        float blend = HoverEase(arrowHoverBlend[handle], arrowHoverEasing);
        point.x += blend * (brushBend + envelope * (arrowCurvature + wave));
        return point;
    }

    private void UpdateArrowMeshes()
    {
        for (int handle = 0; handle < arrowMeshes.Length; handle++)
        {
            bool engaged = drag >= 0 ? drag == handle : hover == handle;
            float previousBlend = arrowHoverBlend[handle];
            float targetBlend = engaged ? 1f : 0f;
            float duration = Mathf.Max(.01f, engaged ? arrowHoverTransition : arrowHoverFadeOut);
            float blend = Mathf.MoveTowards(previousBlend, targetBlend, Time.deltaTime / duration);
            arrowHoverBlend[handle] = blend;
            if (previousBlend == 0f && blend == 0f)
                continue;

            if (animateArrows && blend > 0f)
            {
                // Smooth noise varies the wave without jumping between random values.
                arrowWaveTime[handle] += Time.deltaTime * arrowWaveSpeed;
                float noise = Mathf.Clamp01(Mathf.PerlinNoise(arrowWaveTime[handle] * .41f, 19f + 11f * handle));
                float variation = (2f * noise - 1f) * arrowWaveRandomness;
                arrowWaveVariation[handle] = variation;
                float speed = arrowWaveSpeed * (1f + .55f * variation);
                arrowWavePhase[handle] = Mathf.Repeat(arrowWavePhase[handle] + Time.deltaTime * speed, 1f);
            }

            for (int i = 0; i < originalArrowVertices.Length; i++)
                animatedArrowVertices[i] = BendArrowPoint(handle, originalArrowVertices[i]);

            Mesh mesh = arrowMeshes[handle];
            mesh.SetVertices(animatedArrowVertices);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }
    }

    private void DestroyArrowMeshes()
    {
        foreach (Mesh mesh in arrowMeshes)
            if (mesh)
                Destroy(mesh);
    }

    private static float HoverEase(float amount, float easing)
    {
        return Mathf.Lerp(amount, Mathf.SmoothStep(0f, 1f, amount), easing);
    }

    private Vector3 BendRotationPoint(int axisIndex, Vector3 point)
    {
        float angle = Mathf.Atan2(point.y, point.x);
        float variation = rotateWaveVariation[axisIndex];
        // Keep wave counts integral so the full ring joins cleanly.
        float wave = animateRotationArrows
            ? rotateWaveAmplitude * (1f + .45f * variation) *
              Mathf.Sin(angle * rotateWaveCount - 2f * Mathf.PI * rotateWavePhase[axisIndex])
            : 0f;
        float brushBend = brushWobble * (.55f * Mathf.Sin(3f * angle) + .25f * Mathf.Sin(7f * angle + .4f));
        float blend = HoverEase(rotateHoverBlend[axisIndex], rotateHoverEasing);
        float scale = 1f + blend * (brushBend + rotateCurvature * Mathf.Cos(2f * angle) + wave);
        point.x *= scale;
        point.y *= scale;
        return point;
    }

    private void UpdateRotationMesh()
    {
        for (int axisIndex = 0; axisIndex < arcMeshes.Length; axisIndex++)
        {
            int handle = axisIndex + 3;
            bool engaged = drag >= 0 ? drag == handle : hover == handle;
            float previousBlend = rotateHoverBlend[axisIndex];
            float duration = Mathf.Max(.01f, engaged ? rotateHoverFadeIn : rotateHoverFadeOut);
            float blend = Mathf.MoveTowards(previousBlend, engaged ? 1f : 0f, Time.deltaTime / duration);
            rotateHoverBlend[axisIndex] = blend;
            if (previousBlend == 0f && blend == 0f)
                continue;

            if (animateRotationArrows && blend > 0f)
            {
                rotateWaveTime[axisIndex] += Time.deltaTime * rotateWaveSpeed;
                float noise = Mathf.Clamp01(Mathf.PerlinNoise(rotateWaveTime[axisIndex] * .41f,
                                                             53f + 11f * axisIndex));
                float variation = (2f * noise - 1f) * rotateWaveRandomness;
                rotateWaveVariation[axisIndex] = variation;
                float speed = rotateWaveSpeed * (1f + .55f * variation);
                rotateWavePhase[axisIndex] = Mathf.Repeat(rotateWavePhase[axisIndex] + Time.deltaTime * speed, 1f);
            }

            UpdateRotationStroke(axisIndex, arcMeshes[axisIndex], originalArcVertices, animatedArcVertices);
            if (fullRingWhileDragging && drag == handle)
                UpdateRotationStroke(axisIndex, fullRingMesh, originalRingVertices, animatedRingVertices);
        }
    }

    private void UpdateRotationStroke(int axisIndex, Mesh mesh, Vector3[] original, Vector3[] animated)
    {
        for (int i = 0; i < original.Length; i++)
            animated[i] = BendRotationPoint(axisIndex, original[i]);

        mesh.SetVertices(animated);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
    }

    private void DestroyRotationMeshes()
    {
        foreach (Mesh mesh in arcMeshes)
            if (mesh)
                Destroy(mesh);
        if (fullRingMesh)
            Destroy(fullRingMesh);
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
            cameraController.SetDragInputBlocked(true);
    }
    private void UpdateTarget(Vector2 mouse)
    {
        if (drag >= 6)
        {
            if (!PlaneDragPoint(mouse, out Vector3 point))
                return;
            Vector3 d = point - planeGrab;
            float u = Vector3.Dot(d, planeU), v = Vector3.Dot(d, planeV);
            planeTarget = planeU * u + planeV * v;
            return;
        }
        if (drag < 3)
        {
            if (!AxisParam(mouse, out float s))
                return;
            target = s - grab;
        }
        else
        {
            if (!Radial(mouse, out var r))
                return;
            angle += Vector3.SignedAngle(lastRadial, r, axis) * rotationSensitivity;
            lastRadial = r;
            target = angle;
        }
    }
    private void EndDrag()
    {
        if (drag >= 0 && cameraController)
            cameraController.SetDragInputBlocked(false);
        drag = -1;
    }
    // Convert desired screen pixels to world units using the actual camera projection.
    private void RefreshGizmoMeshes()
    {
        if (!planeMeshes[0] || !Mathf.Approximately(drawnPlaneBorder, planeBorder) ||
            !Mathf.Approximately(drawnPlaneRoundness, planeCornerRoundness) ||
            !Mathf.Approximately(drawnPlaneRoughness, planeBrushRoughness))
            BuildPlaneMeshes();

        float shaft = arrowShaftThickness, head = arrowHeadWidth, ring = ringThickness;
        if (adaptiveGizmoThickness && sizeMode != GizmoSizeMode.WorldSize && selected && cam &&
            cam.pixelHeight > 0)
        {
            Vector3 pivot = ghost ? ghost.Root.position : selected.position;
            float worldPerPixel = CameraController.WorldViewHeightAt(cam, pivot) / cam.pixelHeight;
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
        if (!arrowMeshes[0] || brushChanged || ThicknessChanged(shaft, drawnShaft) || ThicknessChanged(head, drawnHead) ||
            !Mathf.Approximately(arrowHeadLength, drawnHeadLength))
        {
            DestroyArrowMeshes();
            arrowPaths = BuildArrowPaths(head);
            arrowMeshes[0] = BuildBrushMesh("Painted Gizmo Arrow", arrowPaths, Vector3.up, shaft, false);
            for (int i = 1; i < arrowMeshes.Length; i++)
                arrowMeshes[i] = Instantiate(arrowMeshes[0]);
            foreach (Mesh mesh in arrowMeshes)
                mesh.MarkDynamic();
            originalArrowVertices = arrowMeshes[0].vertices;
            animatedArrowVertices = new Vector3[originalArrowVertices.Length];
            drawnShaft = shaft;
            drawnHead = head;
            drawnHeadLength = arrowHeadLength;
        }
        if (!arcMeshes[0] || brushChanged || ThicknessChanged(ring, drawnRing) || !Mathf.Approximately(ringRadius, drawnRadius) ||
            ringSegments != drawnSegments || !Mathf.Approximately(arcDegrees, drawnArc))
        {
            DestroyRotationMeshes();
            ringPaths = BuildRingPaths(arcDegrees);
            arcMeshes[0] = BuildBrushMesh("Painted Gizmo Arc", ringPaths, Vector3.forward, ring * .6f,
                                          arcDegrees >= 360f);
            for (int i = 1; i < arcMeshes.Length; i++)
                arcMeshes[i] = Instantiate(arcMeshes[0]);
            foreach (Mesh mesh in arcMeshes)
                mesh.MarkDynamic();
            fullRingMesh = BuildBrushMesh("Painted Gizmo Ring", BuildRingPaths(360f),
                                          Vector3.forward, ring * .6f, true);
            originalArcVertices = arcMeshes[0].vertices;
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
            shaft[i] = Vector3.forward * Mathf.Lerp(.04f, 1f, t);
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
            arc[i] = radial * ringRadius;
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
