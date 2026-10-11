using LMDVR.Game;
using LMDVR.OpenXR;
using UnityEngine;

namespace LMDVR.Cameras;

internal enum VrCameraMode
{
    Diorama,
    Chase,
    FirstPerson,
    Screen,
}

/// <summary>Where the player's tracked space sits in the game world, and how big they are in it.</summary>
internal struct RigPose
{
    public Vector3 Position;
    public Quaternion Rotation;

    /// <summary>Game units per real meter: above 1 the player is a giant and the world a miniature.</summary>
    public float Scale;
}

/// <summary>
/// Third person built for VR: behind the bike at eye height, horizon level, swinging round after the
/// bike's turns only while it rides, easing in front of terrain that would block the view. The right
/// stick moves it around the bike (and it stays there).
/// </summary>
internal sealed class ChaseCamera
{
    private const float TargetHeight = 1.2f;
    private const float LowestElevation = -10f;
    private const float HighestElevation = 80f;
    private int _collisionMask = -1;
    private Vector3 _target;
    private float _distance;
    private float _yaw;
    private float _yawSpeed;
    private bool _hasTarget;
    private RigPose _last;
    private bool _hasLast;

    /// <summary>How far above the bike the default position is, in degrees seen from the bike.</summary>
    private static float BaseElevation => Mathf.Atan2(Config.ChaseHeight.Value - TargetHeight, Config.ChaseDistance.Value) * Mathf.Rad2Deg;

    /// <summary>Range of the stick's "look up/down": looking up lowers the camera, down raises it.</summary>
    public static float MinLookPitch => BaseElevation - HighestElevation;

    public static float MaxLookPitch => BaseElevation - LowestElevation;

    public void Reset()
    {
        _hasTarget = false;
    }

    /// <param name="look">Right-stick angles in degrees: x turns the view right, y looks up.</param>
    public bool TryGetPose(RiderTracker rider, float dt, Vector2 look, out RigPose pose)
    {
        pose = default;
        if (rider.Bike == null)
        {
            return false;
        }

        if (rider.IsCrashed && _hasLast)
        {
            pose = _last; // hold still while the rider tumbles
            return true;
        }

        if (_collisionMask == -1)
        {
            _collisionMask = LayerMask.GetMask("Ground", "Obstacle", "EnvironmentOther", "Terrain", "Lava");
        }

        // The limits move with the chase distance, which can change after the angle was set.
        look.y = Mathf.Clamp(look.y, MinLookPitch, MaxLookPitch);
        var bikeYaw = rider.Heading(dt).eulerAngles.y;
        var target = rider.Bike.position + Vector3.up * TargetHeight;
        var length = new Vector2(Config.ChaseDistance.Value, Config.ChaseHeight.Value - TargetHeight).magnitude;
        var turnDelay = Config.ChaseTurnDelay.Value;
        if (!_hasTarget)
        {
            _target = target;
            _distance = length;
            _yaw = bikeYaw;
            _yawSpeed = 0f;
            _hasTarget = true;
        }
        else
        {
            // A little lag keeps bumps and jumps from shaking the whole world.
            _target = Vector3.Lerp(_target, target, 1f - Mathf.Exp(-dt * 8f));
            _yaw = turnDelay > 0f ? SpringToAngle(_yaw, bikeYaw, ref _yawSpeed, turnDelay, dt) : bikeYaw;
        }

        var heading = Quaternion.Euler(0f, _yaw + look.x, 0f);

        // Like a regular game camera, the view tilts with the orbit so the rider stays where it was in view.
        var elevation = BaseElevation - look.y;
        var direction = heading * Quaternion.Euler(elevation, 0f, 0f) * Vector3.back;
        var allowed = length;
        if (Physics.Raycast(_target, direction, out var hit, length + 0.3f, _collisionMask))
        {
            allowed = Mathf.Max(0.6f, hit.distance - 0.3f);
        }

        // Pull in at once when something blocks the view, ease back out slowly.
        _distance = allowed < _distance ? allowed : Mathf.Lerp(_distance, allowed, 1f - Mathf.Exp(-dt * 1.5f));

        pose = new RigPose { Position = _target + direction * _distance, Rotation = heading * Quaternion.Euler(-look.y, 0f, 0f), Scale = 1f };
        _last = pose;
        _hasLast = true;
        return true;
    }

    /// <summary>
    /// Critically damped spring towards an angle (the same one as Unity's SmoothDamp): starts gently,
    /// never overshoots, and in a steady turn trails behind by about turn rate × <paramref name="time"/>.
    /// </summary>
    private static float SpringToAngle(float current, float target, ref float speed, float time, float dt)
    {
        var change = Mathf.Repeat(current - target + 180f, 360f) - 180f;
        var omega = 2f / time;
        var x = omega * dt;
        var decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        var temp = (speed + omega * change) * dt;
        speed = (speed - omega * temp) * decay;
        return target + (change + temp) * decay;
    }
}

/// <summary>
/// Stereo from wherever the game (or another camera mod) puts its camera, as a miniature: the scale
/// follows the camera distance so the rider appears about <see cref="Config.DioramaViewDistance"/>
/// meters away. When the game camera cuts (respawn), so does this view.
/// </summary>
internal sealed class DioramaCamera
{
    private const float CutDistance = 4f;

    private Vector3 _position;
    private Vector3 _toRider;
    private float _yaw;
    private float _scale;
    private float _zoom = 1f;
    private Vector3 _lastCameraPosition;
    private bool _has;

    public void Reset()
    {
        _has = false;
    }

    public bool TryGetPose(Camera gameCamera, RiderTracker rider, float dt, out RigPose pose)
    {
        pose = default;
        if (gameCamera == null)
        {
            return false;
        }

        var t = gameCamera.transform;
        var fixedScale = Config.DioramaScale.Value;
        var targetScale = fixedScale > 0f ? fixedScale
            : rider.Bike != null ? Mathf.Clamp(Vector3.Distance(t.position, rider.Bike.position) / Mathf.Max(0.3f, Config.DioramaViewDistance.Value), 1f, 60f)
            : 10f;
        var toRider = rider.Bike != null ? rider.Bike.position + Vector3.up - t.position : t.forward * 10f;
        var zoom = Mathf.Clamp(Config.DioramaZoom.Value, 0.25f, 16f);

        if (!_has)
        {
            _position = t.position;
            _toRider = toRider;
            _yaw = t.eulerAngles.y;
            _scale = targetScale;
            _zoom = zoom;
            _has = true;
        }
        else
        {
            var cut = (t.position - _lastCameraPosition).sqrMagnitude > CutDistance * CutDistance;
            _position = cut ? t.position : Vector3.Lerp(_position, t.position, 1f - Mathf.Exp(-dt * 12f));
            _toRider = cut ? toRider : Vector3.Lerp(_toRider, toRider, 1f - Mathf.Exp(-dt * 4f));
            _yaw = cut ? t.eulerAngles.y : Mathf.LerpAngle(_yaw, t.eulerAngles.y, 1f - Mathf.Exp(-dt * 6f));
            _scale = Mathf.Lerp(_scale, targetScale, 1f - Mathf.Exp(-dt * 0.8f));
            _zoom = Mathf.Lerp(_zoom, zoom, 1f - Mathf.Exp(-dt * 6f));
        }

        _lastCameraPosition = t.position;

        // Zoom moves towards the rider and shrinks the scale as much: the rider looks bigger, just as far away.
        var position = _position + _toRider * (1f - 1f / _zoom);
        var scale = fixedScale > 0f ? _scale : Mathf.Max(1f, _scale / _zoom);
        pose = new RigPose { Position = position, Rotation = Quaternion.Euler(0f, _yaw, 0f), Scale = scale };
        return true;
    }
}

/// <summary>
/// The Screen camera with depth: the game's own view rendered twice, a little apart, for a stereo
/// picture on the virtual screen, like a 3DS. The rider sits on the screen plane, nearer things
/// come out of it and the background goes behind it; at depth 1 the far background is at infinity.
/// </summary>
internal sealed class StereoScreenCamera
{
    private const float ViewerEyeDistance = 0.064f;
    private float _convergence;
    private bool _has;

    public void Reset()
    {
        _has = false;
    }

    public bool TryGetEyes(Camera gameCamera, RiderTracker rider, float aspect, float dt,
        out Vector3 leftPosition, out Vector3 rightPosition, out Quaternion rotation, out XrFovf leftFov, out XrFovf rightFov)
    {
        leftPosition = rightPosition = default;
        rotation = Quaternion.identity;
        leftFov = rightFov = default;
        if (gameCamera == null)
        {
            return false;
        }

        var t = gameCamera.transform;
        var focus = rider.Bike != null ? Vector3.Dot(rider.Bike.position + Vector3.up - t.position, t.forward) : 10f;
        focus = Mathf.Clamp(focus, 1f, 1000f);
        _convergence = _has ? Mathf.Lerp(_convergence, focus, 1f - Mathf.Exp(-dt * 2f)) : focus;
        _has = true;

        // Two parallel cameras; each picture is shifted so the rider shows in the same place for both eyes.
        // At depth 1 the far background is one viewer eye distance apart on the screen (parallel sight lines).
        var up = Mathf.Tan(gameCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var side = up * aspect;
        var separation = Mathf.Clamp01(Config.ScreenDepth.Value) * ViewerEyeDistance * 2f * side * _convergence / Mathf.Max(0.5f, Config.ScreenWidth.Value);
        var shift = separation * 0.5f / _convergence;
        rotation = t.rotation;
        leftPosition = t.position - t.right * (separation * 0.5f);
        rightPosition = t.position + t.right * (separation * 0.5f);
        leftFov = Frustum(side, up, shift);
        rightFov = Frustum(side, up, -shift);
        return true;
    }

    private static XrFovf Frustum(float side, float up, float shift) => new()
    {
        AngleLeft = Mathf.Atan(shift - side),
        AngleRight = Mathf.Atan(shift + side),
        AngleUp = Mathf.Atan(up),
        AngleDown = -Mathf.Atan(up),
    };
}
