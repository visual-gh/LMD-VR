using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace LMDVR.Game;

/// <summary>
/// Finds the rider on the bike and turns it into stable camera anchors.
/// Comfort rules: the view only follows the bike while it is actually moving (no drifting when the
/// rider stops and puts a foot down), freezes while the rider is a ragdoll after a crash, and snaps
/// (instead of sliding) when the bike is teleported back to a checkpoint.
/// </summary>
internal sealed class RiderTracker
{
    private const float TeleportDistance = 4f;

    /// <summary>The Alternative Camera mod's first-person eye point, in the neck bone's own frame.</summary>
    private static readonly Vector3 NeckToEyes = new(0f, 0.3f, 0f);

    private Transform _bike;
    private Rigidbody _body;
    private GameObject _ragdoll;
    private Transform _head;
    private Transform _neck;
    private Vector3 _smoothedLocal;
    private bool _hasSmoothed;
    private float _yaw;
    private bool _hasYaw;
    private Vector3 _lastBikePosition;
    private bool _wasCrashed;
    private Vector3 _frozenPosition;
    private Quaternion _frozenRotation = Quaternion.identity;
    private bool _hasFrozen;
    private bool _loggedEyePoint;

    public Transform Bike => _bike;
    public bool IsValid => _bike != null && (_head != null || _neck != null);

    /// <summary>The rider is a ragdoll (crash) until the game respawns the bike.</summary>
    public bool IsCrashed => _ragdoll != null && _ragdoll.activeInHierarchy;

    public float Speed => _body != null ? _body.velocity.magnitude : 0f;

    /// <summary>Set for one frame when the bike jumped (respawn, checkpoint restart); cleared by the reader.</summary>
    public bool Teleported { get; set; }

    /// <summary>Re-resolves the bike and bones when the bike object changed (new run, respawn).</summary>
    public bool Refresh()
    {
        // Runs every frame: searching the scene each time would cost real time in big levels.
        if (_bike != null && IsValid && _bike.gameObject.activeInHierarchy)
        {
            return true;
        }

        var bikeGo = GameObject.Find("Bike(Clone)");
        if (bikeGo == null)
        {
            Clear();
            return false;
        }

        if (bikeGo.transform == _bike && IsValid)
        {
            return true;
        }

        Clear();
        _bike = bikeGo.transform;
        _body = bikeGo.GetComponent<Rigidbody>();
        _lastBikePosition = _bike.position;
        foreach (var root in bikeGo.scene.GetRootGameObjects())
        {
            if (root.name == "RagDoll(Clone)")
            {
                _ragdoll = root;
            }
        }

        foreach (var c in bikeGo.GetComponentsInChildren(Il2CppType.Of<Transform>(), true))
        {
            var t = c.Cast<Transform>();
            var name = t.name.ToLowerInvariant();
            if (_head == null && name.StartsWith("head") && !name.StartsWith("head_tip") && name.Contains("jnt"))
            {
                _head = t;
            }
            else if (_neck == null && name.StartsWith("neck") && name.Contains("jnt"))
            {
                _neck = t;
            }
        }

        // First person puts the camera inside the merged bike+rider mesh's bounds, which can get it
        // culled; the Alternative Camera mod does the same.
        foreach (var c in bikeGo.GetComponentsInChildren(Il2CppType.Of<SkinnedMeshRenderer>(), true))
        {
            c.Cast<SkinnedMeshRenderer>().updateWhenOffscreen = true;
        }

        Mod.Log.Msg($"VR: rider found: head={_head?.name ?? "-"} neck={_neck?.name ?? "-"} ragdoll={(_ragdoll != null ? "yes" : "no")} rigidbody={(_body != null ? "yes" : "no")}");
        return IsValid;
    }

    /// <summary>Call once per frame before reading anchors: tracks crashes and teleports.</summary>
    public void Update()
    {
        if (_bike == null)
        {
            return;
        }

        var position = _bike.position;
        var crashed = IsCrashed;
        if (crashed != _wasCrashed)
        {
            Mod.Log.Msg(crashed ? "VR: crash, view held still" : "VR: rider back on the bike");
        }

        var jumped = (position - _lastBikePosition).sqrMagnitude > TeleportDistance * TeleportDistance;
        if (jumped || (_wasCrashed && !crashed))
        {
            Teleported = true;
            _hasSmoothed = false;
            _hasYaw = false;
            if (jumped)
            {
                Mod.Log.Msg($"VR: bike moved {(position - _lastBikePosition).magnitude:0.0} m in one frame (respawn), view snapped");
            }
        }

        _wasCrashed = crashed;
        _lastBikePosition = position;
    }

    /// <summary>
    /// Heading of the bike for the camera: follows the bike while it rides, holds still when it stops
    /// (standing bikes wobble), jumps on teleports.
    /// </summary>
    public Quaternion Heading(float dt)
    {
        var bikeYaw = _bike.eulerAngles.y;
        var follow = Config.HeadingFollowSpeed.Value * MovingFactor();
        if (!_hasYaw || Config.HeadingFollowSpeed.Value <= 0f)
        {
            _yaw = bikeYaw;
            _hasYaw = true;
        }
        else if (!IsCrashed)
        {
            _yaw = Mathf.LerpAngle(_yaw, bikeYaw, 1f - Mathf.Exp(-dt * follow));
        }

        return Quaternion.Euler(0f, _yaw, 0f);
    }

    /// <summary>
    /// First-person origin: the Alternative Camera mod's eye point. Head bob is low-pass filtered in
    /// the bike's own frame (the view stays glued to the bike, animation jitter is damped) and frozen
    /// while the bike stands still. During a crash the view freezes where it was until the respawn.
    /// </summary>
    public bool TryGetEyeAnchor(float dt, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (_bike == null || (_neck == null && _head == null))
        {
            return false;
        }

        if (IsCrashed && _hasFrozen)
        {
            position = _frozenPosition;
            rotation = _frozenRotation;
            return true;
        }

        var eyes = _neck != null ? _neck.TransformPoint(NeckToEyes) : _head.position + Vector3.up * 0.2f;
        if (!_loggedEyePoint && _head != null)
        {
            _loggedEyePoint = true;
            var fromHead = Quaternion.Inverse(Quaternion.Euler(0f, _bike.eulerAngles.y, 0f)) * (eyes - _head.position);
            MelonDebug.Msg($"VR: first-person eye point {fromHead.y:0.00} m above, {fromHead.z:0.00} m ahead of, {fromHead.x:0.00} m right of the head joint");
        }

        var local = _bike.InverseTransformPoint(eyes);
        var smoothing = Config.HeadSmoothing.Value;
        if (!_hasSmoothed || smoothing <= 0f)
        {
            _smoothedLocal = local;
            _hasSmoothed = true;
        }
        else
        {
            _smoothedLocal = Vector3.Lerp(_smoothedLocal, local, 1f - Mathf.Exp(-dt * smoothing * MovingFactor()));
        }

        rotation = Heading(dt);
        var offset = new Vector3(0f, Config.EyeOffsetUp.Value, Config.EyeOffsetForward.Value);
        position = _bike.TransformPoint(_smoothedLocal) + rotation * offset;
        _frozenPosition = position;
        _frozenRotation = rotation;
        _hasFrozen = true;
        return true;
    }

    /// <summary>Snap the heading to the bike (after recentering or switching camera).</summary>
    public void SnapHeading() => _hasYaw = false;

    /// <summary>0 when standing still, 1 from about walking pace up; eases the view in and out of following.</summary>
    private float MovingFactor() => Mathf.Clamp01((Speed - 0.4f) / 1.6f);

    private void Clear()
    {
        _bike = null;
        _body = null;
        _ragdoll = null;
        _head = null;
        _neck = null;
        _hasSmoothed = false;
        _hasYaw = false;
        _hasFrozen = false;
        _wasCrashed = false;
        _loggedEyePoint = false;
    }
}
