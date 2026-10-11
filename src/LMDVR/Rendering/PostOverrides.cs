using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace LMDVR.Rendering;

/// <summary>
/// A top-priority global post-processing volume on a layer only the eye cameras listen to.
/// It switches off effects that feel wrong or sickening in a headset, without touching the
/// game's own profiles (the desktop camera keeps its look).
/// </summary>
internal static class PostOverrides
{
    /// <summary>Unused by the game (its layers stop at 25).</summary>
    public const int Layer = 30;

    private static GameObject _go;

    public static void Ensure(bool disableVignette)
    {
        if (_go != null)
        {
            return;
        }

        var profile = ScriptableObject.CreateInstance(Il2CppType.Of<PostProcessProfile>()).Cast<PostProcessProfile>();
        profile.name = "LMDVR_Overrides";
        Disable(profile, Il2CppType.Of<DepthOfField>());
        Disable(profile, Il2CppType.Of<MotionBlur>());
        Disable(profile, Il2CppType.Of<ChromaticAberration>());
        Disable(profile, Il2CppType.Of<LensDistortion>());
        if (disableVignette)
        {
            Disable(profile, Il2CppType.Of<Vignette>());
        }

        // Layer first: the volume registers under its layer when it is enabled.
        _go = new GameObject("LMDVR_PostOverrides") { layer = Layer };
        UnityEngine.Object.DontDestroyOnLoad(_go);
        var volume = _go.AddComponent(Il2CppType.Of<PostProcessVolume>()).Cast<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 10000f;
        volume.weight = 1f;
        volume.sharedProfile = profile;
    }

    private static void Disable(PostProcessProfile profile, Il2CppSystem.Type effect)
    {
        var settings = profile.AddSettings(effect);
        settings.enabled.Override(false);
    }
}
