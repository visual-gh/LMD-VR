using UnityEngine;

namespace LMDVR.Rendering;

/// <summary>
/// Copies the finished game frame (world + UI, exactly what the monitor shows) into a fixed-size
/// texture that the headset displays as a flat virtual screen.
/// </summary>
internal sealed class ScreenMirror
{
    private readonly int _width;
    private readonly int _height;
    private RenderTexture _capture;
    private RenderTexture _output;
    private IntPtr _outputNative;

    public ScreenMirror(int width, int height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>Some graphics APIs hand the back buffer over upside down.</summary>
    public bool FlipVertically { get; set; }

    public RenderTexture Output => _output;

    /// <summary>Must run after all cameras rendered (end of frame). Returns the native texture.</summary>
    public IntPtr Capture()
    {
        EnsureTextures();
        ScreenCapture.CaptureScreenshotIntoRenderTexture(_capture);
        Graphics.Blit(_capture, _output, new Vector2(1f, FlipVertically ? -1f : 1f), new Vector2(0f, FlipVertically ? 1f : 0f));
        return _outputNative;
    }

    private void EnsureTextures()
    {
        if (_capture == null || _capture.width != Screen.width || _capture.height != Screen.height)
        {
            if (_capture != null)
            {
                UnityEngine.Object.Destroy(_capture);
            }

            _capture = NewTexture(Screen.width, Screen.height, "LMDVR_ScreenCapture");
        }

        if (_output == null || !_output.IsCreated())
        {
            if (_output != null)
            {
                UnityEngine.Object.Destroy(_output);
            }

            _output = NewTexture(_width, _height, "LMDVR_VirtualScreen");
            _outputNative = _output.GetNativeTexturePtr();
        }
    }

    internal static RenderTexture NewTexture(int width, int height, string name, int depth = 0)
    {
        var rt = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = name,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
        };
        rt.hideFlags = HideFlags.HideAndDontSave;
        rt.Create();
        return rt;
    }
}
