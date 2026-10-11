using UnityEngine;

namespace LMDVR.OpenXR;

/// <summary>
/// OpenXR is right-handed (-Z forward), Unity left-handed (+Z forward): mirroring Z converts
/// positions as (x, y, -z) and rotations as (-x, -y, z, w).
/// </summary>
internal static class XrMath
{
    public static Vector3 ToUnity(XrVector3f v) => new(v.X, v.Y, -v.Z);

    public static Quaternion ToUnity(XrQuaternionf q) => new(-q.X, -q.Y, q.Z, q.W);

    public static XrVector3f ToXr(Vector3 v) => new() { X = v.x, Y = v.y, Z = -v.z };

    public static XrQuaternionf ToXr(Quaternion q) => new() { X = -q.x, Y = -q.y, Z = q.z, W = q.w };

    /// <summary>Unity (OpenGL-convention) projection for an asymmetric per-eye frustum.</summary>
    public static Matrix4x4 Projection(XrFovf fov, float near, float far)
    {
        var left = Mathf.Tan(fov.AngleLeft);
        var right = Mathf.Tan(fov.AngleRight);
        var up = Mathf.Tan(fov.AngleUp);
        var down = Mathf.Tan(fov.AngleDown);

        var m = new Matrix4x4();
        m.m00 = 2f / (right - left);
        m.m02 = (right + left) / (right - left);
        m.m11 = 2f / (up - down);
        m.m12 = (up + down) / (up - down);
        m.m22 = -(far + near) / (far - near);
        m.m23 = -(2f * far * near) / (far - near);
        m.m32 = -1f;
        return m;
    }

    /// <summary>Vertical field of view in degrees, for effects that read Camera.fieldOfView.</summary>
    public static float VerticalFovDegrees(XrFovf fov) => (fov.AngleUp - fov.AngleDown) * Mathf.Rad2Deg;

    /// <summary>
    /// The same (asymmetric) frustum opened up to a wider vertical angle. An eye rendered with it and
    /// submitted with the headset's own angles squeezes more of the world into the lenses.
    /// </summary>
    public static XrFovf WidenFov(XrFovf fov, float verticalDegrees)
    {
        var target = Mathf.Min(verticalDegrees, 170f) * Mathf.Deg2Rad;
        var up = Mathf.Tan(fov.AngleUp);
        var down = Mathf.Tan(-fov.AngleDown);
        if (up <= 0f || down <= 0f || target <= fov.AngleUp - fov.AngleDown)
        {
            return fov;
        }

        // atan(k * up) + atan(k * down) grows with k: bisect for the tangent scale that gives the target.
        float low = 1f, high = 2f;
        while (Mathf.Atan(high * up) + Mathf.Atan(high * down) < target && high < 1000f)
        {
            high *= 2f;
        }

        for (var i = 0; i < 30; i++)
        {
            var mid = (low + high) * 0.5f;
            if (Mathf.Atan(mid * up) + Mathf.Atan(mid * down) < target)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var k = (low + high) * 0.5f;
        return new XrFovf
        {
            AngleLeft = Mathf.Atan(k * Mathf.Tan(fov.AngleLeft)),
            AngleRight = Mathf.Atan(k * Mathf.Tan(fov.AngleRight)),
            AngleUp = Mathf.Atan(k * up),
            AngleDown = -Mathf.Atan(k * down),
        };
    }

    /// <summary>A symmetric frustum, used by the desktop preview when no headset is connected.</summary>
    public static XrFovf SymmetricFov(float horizontalDegrees, float verticalDegrees)
    {
        var h = horizontalDegrees * 0.5f * Mathf.Deg2Rad;
        var v = verticalDegrees * 0.5f * Mathf.Deg2Rad;
        return new XrFovf { AngleLeft = -h, AngleRight = h, AngleUp = v, AngleDown = -v };
    }

    /// <summary>Keeps only the heading of a rotation (horizon stays level).</summary>
    public static Quaternion YawOnly(Quaternion q)
    {
        var forward = q * Vector3.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
}
