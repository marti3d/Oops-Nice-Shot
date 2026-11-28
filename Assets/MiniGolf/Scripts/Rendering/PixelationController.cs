using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PixelationController : MonoBehaviour
{
    [MiniGolfFoldout("Material")]
    [SerializeField]
    private Material postProcessMaterial;

    [MiniGolfFoldout("Resolution / Pixelation")]
    [Range(90, 1080)]
    public int targetHeight = 240;

    [MiniGolfFoldout("Color Style")]
    [Range(0f, 2f)]
    public float saturation = 1.15f;

    [Range(0.5f, 2f)]
    public float contrast = 1.08f;

    [Range(-0.5f, 0.5f)]
    public float brightness = 0.02f;

    [MiniGolfFoldout("Retro Look")]
    [Range(2, 64)]
    public int colorSteps = 16;

    [Range(0f, 1f)]
    public float ditherStrength = 0.12f;

    [MiniGolfFoldout("Update")]
    public bool updateEveryFrame = true;

    private static readonly int TargetHeight =
        Shader.PropertyToID("_TargetHeight");

    private static readonly int Saturation =
        Shader.PropertyToID("_Saturation");

    private static readonly int Contrast =
        Shader.PropertyToID("_Contrast");

    private static readonly int Brightness =
        Shader.PropertyToID("_Brightness");

    private static readonly int ColorSteps =
        Shader.PropertyToID("_ColorSteps");

    private static readonly int DitherStrength =
        Shader.PropertyToID("_DitherStrength");

    private void OnEnable()
    {
        UpdateMaterial();
    }

    private void OnValidate()
    {
        UpdateMaterial();
    }

    private void Update()
    {
        if (updateEveryFrame)
            UpdateMaterial();
    }

    private void UpdateMaterial()
    {
        if (postProcessMaterial == null)
            return;

        postProcessMaterial.SetFloat(TargetHeight, targetHeight);
        postProcessMaterial.SetFloat(Saturation, saturation);
        postProcessMaterial.SetFloat(Contrast, contrast);
        postProcessMaterial.SetFloat(Brightness, brightness);
        postProcessMaterial.SetFloat(ColorSteps, colorSteps);
        postProcessMaterial.SetFloat(DitherStrength, ditherStrength);
    }

    [ContextMenu("Apply Subtle Preset")]
    private void ApplySubtlePreset()
    {
        targetHeight = 360;
        saturation = 1.08f;
        contrast = 1.04f;
        brightness = 0.01f;
        colorSteps = 24;
        ditherStrength = 0.06f;

        UpdateMaterial();
    }

    [ContextMenu("Apply A Short Hike Inspired Preset")]
    private void ApplyShortHikeInspiredPreset()
    {
        targetHeight = 240;
        saturation = 1.15f;
        contrast = 1.08f;
        brightness = 0.02f;
        colorSteps = 16;
        ditherStrength = 0.12f;

        UpdateMaterial();
    }

    [ContextMenu("Apply Chunky Retro Preset")]
    private void ApplyChunkyRetroPreset()
    {
        targetHeight = 180;
        saturation = 1.2f;
        contrast = 1.12f;
        brightness = 0.015f;
        colorSteps = 12;
        ditherStrength = 0.18f;

        UpdateMaterial();
    }
}