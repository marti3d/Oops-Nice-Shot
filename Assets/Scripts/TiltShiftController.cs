
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class TiltShiftController : MonoBehaviour
{
    [Header("Material")]
    [SerializeField]
    private Material tiltShiftMaterial;

    [Header("Focus")]
    [Range(0f, 1f)]
    public float focusPosition = 0.5f;

    [Range(0.01f, 1f)]
    public float focusWidth = 0.12f;

    [Header("Blur")]
    [Range(0f, 10f)]
    public float blurStrength = 6f;

    [Range(0.001f, 0.5f)]
    public float transition = 0.35f;

    [Header("Color")]
    [Range(0f, 2f)]
    public float saturation = 1.15f;

    private static readonly int FocusPosition =
        Shader.PropertyToID("_FocusPosition");

    private static readonly int FocusWidth =
        Shader.PropertyToID("_FocusWidth");

    private static readonly int BlurStrength =
        Shader.PropertyToID("_BlurStrength");

    private static readonly int Transition =
        Shader.PropertyToID("_Transition");

    private static readonly int Saturation =
        Shader.PropertyToID("_Saturation");

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
        UpdateMaterial();
    }

    private void UpdateMaterial()
    {
        if (tiltShiftMaterial == null)
            return;

        tiltShiftMaterial.SetFloat(
            FocusPosition,
            focusPosition
        );

        tiltShiftMaterial.SetFloat(
            FocusWidth,
            focusWidth
        );

        tiltShiftMaterial.SetFloat(
            BlurStrength,
            blurStrength
        );

        tiltShiftMaterial.SetFloat(
            Transition,
            transition
        );

        tiltShiftMaterial.SetFloat(
            Saturation,
            saturation
        );
    }
}
