using LMDVR.Cameras;
using MelonLoader;
using MelonLoader.Utils;

namespace LMDVR;

internal static class Config
{
    private static MelonPreferences_Category _category;

    public static MelonPreferences_Entry<bool> AutoStart { get; private set; }
    public static MelonPreferences_Entry<float> ResolutionScale { get; private set; }
    public static MelonPreferences_Entry<bool> BorderlessInVr { get; private set; }
    public static MelonPreferences_Entry<bool> FlipEyes { get; private set; }

    public static MelonPreferences_Entry<VrCameraMode> CameraMode { get; private set; }

    public static MelonPreferences_Entry<float> FirstPersonFov { get; private set; }
    public static MelonPreferences_Entry<float> FirstPersonNearClip { get; private set; }
    public static MelonPreferences_Entry<float> WorldScale { get; private set; }
    public static MelonPreferences_Entry<float> EyeOffsetUp { get; private set; }
    public static MelonPreferences_Entry<float> EyeOffsetForward { get; private set; }
    public static MelonPreferences_Entry<float> HeadSmoothing { get; private set; }
    public static MelonPreferences_Entry<float> HeadingFollowSpeed { get; private set; }
    public static MelonPreferences_Entry<bool> PauseCameraMods { get; private set; }
    public static MelonPreferences_Entry<float> NearClip { get; private set; }
    public static MelonPreferences_Entry<bool> DisableVignette { get; private set; }

    public static MelonPreferences_Entry<float> ChaseDistance { get; private set; }
    public static MelonPreferences_Entry<float> ChaseHeight { get; private set; }
    public static MelonPreferences_Entry<float> ChaseTurnDelay { get; private set; }

    public static MelonPreferences_Entry<float> DioramaZoom { get; private set; }
    public static MelonPreferences_Entry<float> DioramaScale { get; private set; }
    public static MelonPreferences_Entry<float> DioramaViewDistance { get; private set; }

    public static MelonPreferences_Entry<float> ScreenDepth { get; private set; }

    public static MelonPreferences_Entry<float> HudDistance { get; private set; }
    public static MelonPreferences_Entry<float> HudWidth { get; private set; }
    public static MelonPreferences_Entry<float> HudHeight { get; private set; }

    public static MelonPreferences_Entry<float> ScreenDistance { get; private set; }
    public static MelonPreferences_Entry<float> ScreenWidth { get; private set; }
    public static MelonPreferences_Entry<bool> FlipScreenCapture { get; private set; }

    public static void Init()
    {
        _category = MelonPreferences.CreateCategory("LMDVR", "LMD VR");
        _category.SetFilePath(Path.Combine(MelonEnvironment.UserDataDirectory, "LMDVR.cfg"));

        AutoStart = _category.CreateEntry("AutoStart", true, "Auto start",
            "Connect to the headset automatically when the game starts (F9 toggles VR at any time).");
        ResolutionScale = _category.CreateEntry("ResolutionScale", 1.0f, "Resolution scale",
            "Per-eye resolution relative to what the headset recommends. Lower = faster (0.7 - 1.3).");
        BorderlessInVr = _category.CreateEntry("BorderlessInVr", true, "Borderless window in VR",
            "Switch exclusive fullscreen to a borderless window when VR starts, so the game keeps running when the headset has focus.");
        FlipEyes = _category.CreateEntry("FlipEyes", true, "Flip eye images",
            "Vertical flip of the eye images for the headset. Only change if the world shows upside down in VR.");

        CameraMode = _category.CreateEntry("CameraMode", VrCameraMode.Diorama, "Camera while riding",
            "Diorama (the game's own camera in stereo, a miniature world), Chase (third person, behind the bike), FirstPerson (the rider's view) or Screen (the game on a virtual screen, flat or 3D). F5 / LB+RB+D-pad switch in game.");

        FirstPersonFov = _category.CreateEntry("FirstPersonFov", 0f, "First person field of view",
            "Vertical degrees, like the Alternative Camera mod's FoV (LB+RB+D-pad up/down in first person). 0 = the headset's own, true to life. " +
            "Wider shows more of the trail, but the world looks farther away and swims a little when you turn your head.");
        FirstPersonNearClip = _category.CreateEntry("FirstPersonNearClip", 0.65f, "First person near clip",
            "First person: nothing closer than this many meters is drawn, which cuts the rider's body away at the waist when you look down " +
            "(the Alternative Camera mod uses 0.6; from 0.7 the tops of the gloves get cut when you look straight down).");
        WorldScale = _category.CreateEntry("WorldScale", 1.0f, "World scale (first person)",
            "Multiplies head movement and eye distance in first person. Above 1 the world looks smaller, below 1 bigger.");
        EyeOffsetUp = _category.CreateEntry("EyeOffsetUp", 0f, "Eye offset up",
            "First person: meters to raise your eyes above the Alternative Camera mod's eye point (about 30 cm behind the rider's head).");
        EyeOffsetForward = _category.CreateEntry("EyeOffsetForward", 0f, "Eye offset forward",
            "First person: meters to move your eyes forward from that eye point.");
        HeadSmoothing = _category.CreateEntry("HeadSmoothing", 12f, "Head bob smoothing",
            "How fast the view follows the rider's head animation (lower = calmer, 0 = no smoothing). The view holds still while the bike stands.");
        HeadingFollowSpeed = _category.CreateEntry("HeadingFollowSpeed", 10f, "Heading follow speed",
            "First person and chase: how fast the view turns with the bike (lower = smoother turns, 0 = rigid). No turning while the bike stands or during crashes.");
        PauseCameraMods = _category.CreateEntry("PauseCameraMods", true, "Pause other camera mods in VR",
            "While the headset is on, put the Alternative Camera mod back on the game's camera and pause it (its L3/R3 buttons would switch the camera). It resumes when VR stops.");
        NearClip = _category.CreateEntry("NearClip", 0.05f, "Near clip",
            "Chase and diorama: closest distance in meters that is still drawn.");
        DisableVignette = _category.CreateEntry("DisableVignette", true, "Disable vignette",
            "Remove the dark screen edges in the headset.");

        ChaseDistance = _category.CreateEntry("ChaseDistance", 3.5f, "Chase distance",
            "Chase camera: meters behind the bike (LB+RB+D-pad up/down).");
        ChaseHeight = _category.CreateEntry("ChaseHeight", 1.9f, "Chase height",
            "Chase camera: eye height in meters above the bike.");
        ChaseTurnDelay = _category.CreateEntry("ChaseTurnDelay", 0.3f, "Chase turn delay",
            "Chase camera: how far it lags behind the bike's turns, in seconds, before swinging round (0 = turns with the bike at once).");

        DioramaZoom = _category.CreateEntry("DioramaZoom", 1f, "Diorama zoom",
            "Diorama: how much bigger the rider and the mountain around it look than from the game's camera (LB+RB+D-pad up/down). 1 = the game's framing.");
        DioramaScale = _category.CreateEntry("DioramaScale", 0f, "Diorama scale",
            "Diorama: how many times smaller the world looks. 0 = automatic, from how far the game camera is.");
        DioramaViewDistance = _category.CreateEntry("DioramaViewDistance", 2.5f, "Diorama viewing distance",
            "Diorama (automatic scale): how far away the rider appears, in meters.");

        ScreenDepth = _category.CreateEntry("ScreenDepth", 0f, "Screen depth",
            "Screen camera: 3D depth of the game image on the virtual screen, like a 3DS (LB+RB+D-pad up/down). 0 = flat, the game's own image; 1 = deepest.");

        HudDistance = _category.CreateEntry("HudDistance", 1.4f, "HUD distance",
            "Distance in meters of the panel showing the HUD and pause menu while riding.");
        HudWidth = _category.CreateEntry("HudWidth", 1.6f, "HUD width",
            "Width in meters of the HUD / pause menu panel.");
        HudHeight = _category.CreateEntry("HudHeight", -0.05f, "HUD height",
            "Vertical offset in meters of the HUD panel relative to your eyes.");

        ScreenDistance = _category.CreateEntry("ScreenDistance", 3.0f, "Virtual screen distance",
            "Distance in meters of the virtual screen (menus and the Screen camera).");
        ScreenWidth = _category.CreateEntry("ScreenWidth", 3.2f, "Virtual screen width",
            "Width in meters of the virtual screen (right stick up/down while riding with the Screen camera).");
        FlipScreenCapture = _category.CreateEntry("FlipScreenCapture", false, "Flip virtual screen",
            "Flip the virtual screen vertically (only if it shows upside down).");

        _category.SaveToFile(false);
    }

    public static void Save() => _category.SaveToFile(false);

    /// <summary>Picks up edits made to LMDVR.cfg while the game runs.</summary>
    public static void Reload() => _category.LoadFromFile(false);
}
