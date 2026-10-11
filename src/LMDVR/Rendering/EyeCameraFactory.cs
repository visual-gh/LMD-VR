using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace LMDVR.Rendering;

/// <summary>
/// Builds eye cameras as clones of the game's own camera, so both eyes get exactly the same
/// rendering setup (deferred path, post-processing stack, flares, ...), minus the scripts that
/// would move the camera, listen to audio or re-configure post effects behind our back.
/// </summary>
internal static class EyeCameraFactory
{
    // Matched against the component's full type name.
    private static readonly string[] RemovedComponents =
    {
        "Cameras.PlayCamera", "Cameras.MenuCamera", "Cameras.CameraBase", "Cameras.DynamicCamera",
        "Cameras.CamShake", "Cameras.FullscreenEffectQuad", "PostEffectConfigurator",
        "PlayerCameraTarget", "StudioListener", "AudioListener", "AudioBehaviour",
        "RandomSinMovement", "CameraHack", "FOVOverride",
        "Graphics.Screenshot", "ImageEffect_DistanceBandVisualizer",
    };

    private static readonly string[] KeptComponents =
    {
        "UnityEngine.Transform", "UnityEngine.Camera", "UnityEngine.FlareLayer",
        "UnityEngine.Rendering.PostProcessing.PostProcessLayer",
    };

    /// <summary>
    /// <paramref name="cullingMask"/>: what the game camera normally draws (it draws nothing while VR has
    /// taken it over). <paramref name="overrideVolumeLayer"/>: extra post-processing volume layer, or -1.
    /// </summary>
    public static Camera Create(Camera source, string name, RenderTexture target, int cullingMask, int overrideVolumeLayer)
    {
        // Instantiating under an inactive parent defers Awake/OnEnable of every cloned script,
        // so game scripts on the clone never run before we remove them.
        var staging = new GameObject("LMDVR_Staging");
        staging.SetActive(false);
        var clone = UnityEngine.Object.Instantiate(source.gameObject, staging.transform, false).Cast<GameObject>();
        clone.name = name;

        for (var i = clone.transform.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.DestroyImmediate(clone.transform.GetChild(i).gameObject);
        }

        StripComponents(clone);

        var cam = clone.GetComponent<Camera>();
        cam.targetTexture = target;
        cam.cullingMask = cullingMask;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.depth = source.depth + 0.5f;
        cam.enabled = true;

        var ppl = clone.GetComponent<PostProcessLayer>();
        if (ppl != null)
        {
            // TAA rebuilds the projection from fieldOfView every frame, which would throw away the
            // asymmetric per-eye frustum. SMAA is jitter-free and cheap.
            ppl.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
            ppl.volumeTrigger = clone.transform;
            if (overrideVolumeLayer >= 0)
            {
                var mask = ppl.volumeLayer;
                mask.value |= 1 << overrideVolumeLayer;
                ppl.volumeLayer = mask;
            }
        }

        clone.transform.SetParent(null, false);
        UnityEngine.Object.Destroy(staging);
        UnityEngine.Object.DontDestroyOnLoad(clone);
        return cam;
    }

    private static void StripComponents(GameObject go)
    {
        var removed = new List<string>();

        // Two passes: a component another one requires (RequireComponent) can only go once its dependent is gone.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var c in go.GetComponents(Il2CppType.Of<Component>()))
            {
                var typeName = c == null ? null : c.GetIl2CppType().FullName;
                if (typeName == null || KeptComponents.Contains(typeName) || !RemovedComponents.Any(typeName.Contains))
                {
                    continue;
                }

                try
                {
                    UnityEngine.Object.DestroyImmediate(c);
                    removed.Add(typeName);
                }
                catch (Exception ex)
                {
                    if (pass == 1)
                    {
                        Mod.Log.Warning($"VR: could not remove {typeName} from eye camera: {ex.Message}");
                    }
                }
            }
        }

        // Anything neither kept nor removed on purpose stays (it may be a render effect); the debug log lists it.
        var remaining = go.GetComponents(Il2CppType.Of<Component>())
            .Where(c => c != null)
            .Select(c => c.GetIl2CppType().FullName)
            .Select(n => KeptComponents.Contains(n) ? n : n + " (unknown, kept)");
        MelonDebug.Msg($"VR: eye camera '{go.name}': removed [{string.Join(", ", removed)}], remaining [{string.Join(", ", remaining)}]");
    }
}
