using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class ObjectGizmoController
{
    private void Confirm()
    {
        if (paused || Time.timeScale == 0f || !ghost)
            return;
        if (!HasGround || !PreviewValid)
        {
            rejectedUntil = Time.time + rejectDuration;
            return;
        }
        EndDrag();
        var t = new Transfer
        {
            body = selected,
            preview = ghost,
            shapes = shapes,
            start = new Pose(selected.position, selected.rotation),
            end = new Pose(ghost.Root.position, ghost.Root.rotation),
            gravity = savedGravity,
            ccd = savedCcd,
            interpolation = savedInterpolation,
            collisions = selected.detectCollisions
        };
        t.duration = Mathf.Clamp(
            Vector3.Distance(t.start.position, t.end.position) / Mathf.Max(.01f, transferSpeed), .1f, .5f);
        selected.detectCollisions = false;
        ghost.SetAppearance(validColor, 0);
        transfers.Add(t);
        ghost = null;
        selected = null;
        shapes = null;
        remainingLift = 0;
        hover = -1;
        selectedObjectSize = 1;
        ReleaseSelectionOutlines();
        HasGround = PreviewValid = false;
    }
    private void UpdateTransfers()
    {
        for (int i = transfers.Count - 1; i >= 0; i--)
        {
            var t = transfers[i];
            if (!t.body)
            {
                if (t.preview)
                    Destroy(t.preview.gameObject);
                transfers.RemoveAt(i);
                continue;
            }
            if (!t.arrived)
            {
                t.elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(t.elapsed / t.duration);
                u = u * u * (3 - 2 * u);
                t.body.position = Vector3.Lerp(t.start.position, t.end.position, u);
                t.body.rotation = Quaternion.Slerp(t.start.rotation, t.end.rotation, u);
                if (t.preview)
                {
                    Color tint = validColor;
                    tint.a *= 1f - u;
                    t.preview.SetAppearance(tint, 0f);
                }
                if (t.elapsed < t.duration)
                    continue;

                // Recheck destination, including other transfers which have already arrived.
                if (!TransferDestinationClear(t))
                {
                    t.body.position = t.start.position;
                    t.body.rotation = t.start.rotation;
                    FinishTransfer(t);
                    transfers.RemoveAt(i);
                    continue;
                }
                t.arrived = true;
                t.body.detectCollisions = t.collisions;
                if (t.preview)
                    Destroy(t.preview.gameObject);
                t.preview = null;
                continue;
            }
            t.hold += Time.deltaTime;
            if (t.hold >= releaseDelay)
            {
                FinishTransfer(t);
                transfers.RemoveAt(i);
            }
        }
    }
    private bool TransferDestinationClear(Transfer t)
    {
        t.body.detectCollisions = true;
        Physics.SyncTransforms();
        return probe.CanPlace(t.body, t.shapes, t.body.position, t.body.rotation, obstacleLayers,
                              collisionClearance);
    }
    private void FinishTransfer(Transfer t)
    {
        if (t.preview)
            Destroy(t.preview.gameObject);
        if (!t.body)
            return;
        if (!t.arrived)
        {
            t.body.position = t.start.position;
            t.body.rotation = t.start.rotation;
        }
        t.body.detectCollisions = t.collisions;
        t.body.isKinematic = false;
        t.body.collisionDetectionMode = t.ccd;
        t.body.interpolation = t.interpolation;
        t.body.useGravity = t.gravity;
        t.body.linearVelocity = Vector3.zero;
        t.body.angularVelocity = Vector3.zero;
        t.body.WakeUp();
    }
    public void RegisterSpawn(Rigidbody rb)
    {
        if (rb && !rb.isKinematic && !spawnPoses.ContainsKey(rb) &&
            (selectableLayers.value & (1 << rb.gameObject.layer)) != 0)
            spawnPoses.Add(rb, new Pose(rb.position, rb.rotation));
    }
    public bool Respawn(Rigidbody rb)
    {
        if (!rb || !spawnPoses.TryGetValue(rb, out var pose))
            return false;
        if (rb == selected)
            ClearSelection();
        for (int i = transfers.Count - 1; i >= 0; i--)
            if (transfers[i].body == rb)
            {
                FinishTransfer(transfers[i]);
                transfers.RemoveAt(i);
            }
        if (rb.isKinematic)
            return false;
        rb.position = pose.position;
        rb.rotation = pose.rotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.WakeUp();
        return true;
    }
}
