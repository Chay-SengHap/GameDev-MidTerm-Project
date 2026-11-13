using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A self-driving car. Added automatically to every vehicle by <see cref="TrafficSystem"/>.
///  - Keeps to the right lane by holding a fixed distance from the right curb.
///  - At junctions it randomly goes straight, turns right or turns left (if that road exists).
///  - Follows bends, makes a U-turn at dead ends.
///  - Obeys traffic lights: stops at the stop line on red, and on yellow if it can stop comfortably.
///  - Car-following uses the Intelligent Driver Model (as used in traffic simulation): it matches the
///    speed of the car in front, keeps a time gap, and brakes smoothly. Every driver is a bit different.
///  - Tail/brake lights, blinking turn signals, nose-dive when braking, body roll in turns,
///    and honks at the player when they block the road.
/// The car is a kinematic Rigidbody: it pushes the player out of the way but isn't knocked around itself.
/// Kinematic bodies pass through each other, so before every move a car checks that its outline won't overlap any
/// other car's outline - cars can never drive into each other, and a car that blocks another for too long backs up.
/// </summary>
[DisallowMultipleComponent]
public class TrafficCar : MonoBehaviour
{
    private enum State { Cruise, Crossing, Turning, Parked }

    public float Speed => speed;
    /// True while stopped at a red light, or queued behind a car that is.
    public bool WaitingAtLight { get; private set; }
    public Vector3 Direction => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

    private TrafficSystem sys;
    private Rigidbody rb;

    private Bounds localBounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(2.8f, 2f, 6f));
    private float halfLength = 3f, halfWidth = 1.4f, height = 2f;
    private Vector3 pos;
    private float yaw;              // direction the car is actually driving (degrees)
    private Vector3 heading;        // the road direction it is following (one of the 4 axes)
    private float speed, accel;
    private State state = State.Cruise;

    // Driver personality
    private float desiredSpeedFactor = 1f, headway = 1.4f, minGap = 2.5f, maxAccel = 3f;

    // Sensing results
    private float curbDist = -1f;   // distance from the centre line to the right curb (-1 = none, junction)
    private float leftDist = -1f;
    private float aheadDist = -1f;  // distance to the end of the road ahead (-1 = road continues)
    private float roadWidth = 13f;
    private float senseTimer;
    private int senseCount;
    private float sinceManeuver;    // metres driven since the last turn / junction

    // Traffic lights
    private TrafficIntersection lightAhead;
    private float stopLineDist;     // from the front bumper
    private TrafficIntersection goingThroughOnYellow;

    // Turn manoeuvre
    private Vector3 nextHeading;
    private float turnSign;         // +1 right, -1 left
    private float turnRemaining;    // degrees still to turn
    private float turnDelay;        // metres to drive straight before turning
    private float currentTurnRadius;

    // Obstacles
    private float blockedTimer, ignoreObstaclesTimer;
    private bool blockedByPlayer;
    private float hornCooldown;
    private AudioSource horn;
    private readonly RaycastHit[] obstacleHits = new RaycastHit[16];

    // Hard no-overlap check against every other car (see WouldOverlapCar).
    private static readonly List<TrafficCar> AllCars = new List<TrafficCar>();
    private const float OverlapMargin = 0.15f;   // extra space kept around each car (m)
    private Vector3 footprintOffset;              // centre of the car's outline in its own space (scaled)
    private float footprintRadius = 4f;           // for a quick distance reject
    private float hardBlockedTimer, backUpTimer;
    private TrafficCar hardBlocker;

    // Anti-gridlock (see TrafficSystem "Anti-gridlock")
    /// True while blocked by the player, or queued behind a car that is.
    public bool QueuedBehindPlayer { get; private set; }
    private float jamTimer, clearRun, junctionHoldTimer, ghostTimer;
    private TrafficCar ghostThrough;   // a stuck car we are allowed to drive past

    // Visuals
    private Renderer[] brakeLights, leftIndicators, rightIndicators;
    private bool brakeOn;
    private float pitch, roll;
    private static readonly Color TailRed = new Color(1f, 0.05f, 0.02f);
    private static readonly Color Amber = new Color(1f, 0.55f, 0f);

    private void Start()
    {
        sys = TrafficSystem.Instance;
        if (sys == null) { enabled = false; return; }

        MeasureSize();

        // Every driver is a little different.
        float v = sys.driverVariation;
        desiredSpeedFactor = Random.Range(1f - v, 1f + v);
        headway = sys.timeHeadway * Random.Range(1f - v * 2f, 1f + v * 2f);
        minGap = sys.safeGap * Random.Range(0.8f, 1.3f);
        maxAccel = sys.acceleration * Random.Range(1f - v, 1f + v);

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Optional per-vehicle behaviour (see TrafficVehicleSettings).
        var settings = GetComponent<TrafficVehicleSettings>();
        if (settings != null)
        {
            desiredSpeedFactor *= settings.SpeedMultiplier;
            if (settings.Behaviour == TrafficBehaviour.Stationary)
            {
                state = State.Parked;   // stays exactly where it was placed, as a solid obstacle
                return;
            }
        }

        senseTimer = Random.Range(0f, sys.senseInterval);   // spread sensing over frames
        if (!PlaceOnRoad())
        {
            state = State.Parked;   // no road nearby: stays parked
            return;
        }
        CreateLights();
        Sense();
    }

    // ---------------------------------------------------------------- setup
    private void MeasureSize()
    {
        // Size of all meshes in the car's own space (independent of its current rotation).
        bool any = false;
        Bounds b = new Bounds();
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(local, Vector3.zero); any = true; }
                else b.Encapsulate(local);
            }
        }
        if (!any) return;
        localBounds = b;
        Vector3 s = transform.lossyScale;
        halfLength = b.extents.z * Mathf.Abs(s.z);
        halfWidth = b.extents.x * Mathf.Abs(s.x);
        height = b.size.y * Mathf.Abs(s.y);
        footprintOffset = new Vector3(b.center.x * s.x, 0f, b.center.z * s.z);
        footprintRadius = Mathf.Sqrt(halfLength * halfLength + halfWidth * halfWidth) + footprintOffset.magnitude;
    }

    private void OnEnable() { if (!AllCars.Contains(this)) AllCars.Add(this); }
    private void OnDisable() { AllCars.Remove(this); }

    private void CreateLights()
    {
        Bounds b = localBounds;
        Vector3 s = transform.lossyScale;
        float inset = 0.45f / Mathf.Max(Mathf.Abs(s.x), 1e-4f);
        float rearZ = sys.modelFacesPositiveZ ? b.min.z : b.max.z;
        float frontZ = sys.modelFacesPositiveZ ? b.max.z : b.min.z;
        float y = b.min.y + b.size.y * 0.32f;
        float leftX = sys.modelFacesPositiveZ ? b.min.x : b.max.x;
        float rightX = sys.modelFacesPositiveZ ? b.max.x : b.min.x;
        float inward = Mathf.Sign(rightX - leftX);   // from the left side toward the right side

        brakeLights = new[]
        {
            TrafficSystem.CreateLamp(transform, new Vector3(leftX + inward * inset * 1.8f, y, rearZ), 0.32f),
            TrafficSystem.CreateLamp(transform, new Vector3(rightX - inward * inset * 1.8f, y, rearZ), 0.32f),
        };
        leftIndicators = new[]
        {
            TrafficSystem.CreateLamp(transform, new Vector3(leftX + inward * inset * 0.6f, y, rearZ), 0.24f),
            TrafficSystem.CreateLamp(transform, new Vector3(leftX + inward * inset * 0.6f, y, frontZ), 0.24f),
        };
        rightIndicators = new[]
        {
            TrafficSystem.CreateLamp(transform, new Vector3(rightX - inward * inset * 0.6f, y, rearZ), 0.24f),
            TrafficSystem.CreateLamp(transform, new Vector3(rightX - inward * inset * 0.6f, y, frontZ), 0.24f),
        };
        foreach (var r in brakeLights) r.sharedMaterial = TrafficSystem.LampMaterial(TailRed, false);
        foreach (var r in leftIndicators) { r.sharedMaterial = TrafficSystem.LampMaterial(Amber, true); r.enabled = false; }
        foreach (var r in rightIndicators) { r.sharedMaterial = TrafficSystem.LampMaterial(Amber, true); r.enabled = false; }
    }

    private bool PlaceOnRoad()
    {
        pos = transform.position;
        pos.y = 0f;

        // Find road under or near the car.
        if (!sys.IsRoad(pos))
        {
            bool found = false;
            for (float r = 2f; r <= 20f && !found; r += 2f)
                for (int a = 0; a < 16 && !found; a++)
                {
                    float ang = a * Mathf.PI * 2f / 16f;
                    Vector3 p = pos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    if (sys.IsRoad(p)) { pos = p; found = true; }
                }
            if (!found) return false;
        }

        // Pick the road direction closest to where the car is facing.
        float facing = transform.eulerAngles.y + (sys.modelFacesPositiveZ ? 0f : 180f);
        Vector3 best = Vector3.zero;
        float bestAngle = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward;
            if (!sys.IsRoad(pos + d * 8f) || !sys.IsRoad(pos + d * 14f)) continue;
            float diff = Mathf.Abs(Mathf.DeltaAngle(facing, i * 90f));
            if (diff < bestAngle) { bestAngle = diff; best = d; }
        }
        if (best == Vector3.zero) return false;

        heading = best;
        yaw = HeadingYaw(heading);

        // Move into the right-hand lane.
        float curb = ScanEdge(pos, Right(heading), sys.maxCurbScan);
        if (curb > 0f) pos += Right(heading) * (curb - sys.laneOffsetFromCurb);

        transform.SetPositionAndRotation(pos, CarRotation());
        rb.position = pos;
        rb.rotation = CarRotation();
        return true;
    }

    // ---------------------------------------------------------------- per physics step
    private void FixedUpdate()
    {
        if (state == State.Parked) return;
        float dt = Time.fixedDeltaTime;

        senseTimer -= dt;
        if (senseTimer <= 0f)
        {
            senseTimer = sys.senseInterval;
            Sense();
            Decide();
        }

        float prevYaw = yaw;
        Steer(dt);
        UpdateSpeed(dt);

        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        float step = speed * dt;

        // Never drive into another car: if this step would make the outlines overlap, stay put.
        if (step > 0f && WouldOverlapCar(pos + dir * step, CarRotation(), out TrafficCar blocker))
        {
            step = 0f;
            speed = 0f;
            accel = -sys.braking;
            hardBlocker = blocker;
            hardBlockedTimer += dt;

            // Two cars blocking each other (e.g. nose to nose in a junction): the lower-priority one backs up.
            if (hardBlockedTimer > 2.5f && blocker != null && blocker.hardBlocker == this &&
                GetInstanceID() > blocker.GetInstanceID())
            {
                backUpTimer = 2f;
                hardBlockedTimer = 0f;
            }
            else
            {
                ResolveJam(blocker, dt);
            }
        }
        else if (step > 0f)
        {
            hardBlockedTimer = 0f;
            hardBlocker = null;
            clearRun += step;
            if (clearRun > 3f) jamTimer = 0f;   // really moving again
        }

        if (ghostTimer > 0f)
        {
            ghostTimer -= dt;
            if (ghostTimer <= 0f) ghostThrough = null;
        }

        if (backUpTimer > 0f)
        {
            backUpTimer -= dt;
            Vector3 back = pos - dir * (1.2f * dt);
            if (!WouldOverlapCar(back, CarRotation(), out _)) pos = back;
            else backUpTimer = 0f;
        }
        pos += dir * step;
        sinceManeuver += step;
        stopLineDist -= step;

        // Keep the curb estimate current between sensing updates.
        if (curbDist > 0f)
            curbDist -= step * Mathf.Sin(Mathf.DeltaAngle(HeadingYaw(heading), yaw) * Mathf.Deg2Rad);

        // Body motion: nose dips when braking / lifts when accelerating, leans out of turns.
        float yawRate = Mathf.DeltaAngle(prevYaw, yaw) / dt * Mathf.Deg2Rad;
        float lateralAccel = speed * yawRate;
        pitch = Mathf.Lerp(pitch, Mathf.Clamp(-accel * 0.35f, -2f, 2.5f), 1f - Mathf.Exp(-6f * dt));
        roll = Mathf.Lerp(roll, Mathf.Clamp(lateralAccel * 0.6f, -3f, 3f), 1f - Mathf.Exp(-6f * dt));

        rb.MovePosition(pos);
        rb.MoveRotation(CarRotation());
    }

    private void Update()
    {
        if (state == State.Parked || brakeLights == null) return;

        // Brake lights: on while slowing down or standing still.
        bool braking = accel < -0.6f || speed < 0.3f;
        if (braking != brakeOn)
        {
            brakeOn = braking;
            var mat = TrafficSystem.LampMaterial(TailRed, braking);
            foreach (var r in brakeLights) r.sharedMaterial = mat;
        }

        // Turn signals blink before and during a turn.
        bool blink = state == State.Turning && Mathf.Repeat(Time.time, 0.7f) < 0.35f;
        foreach (var r in leftIndicators) r.enabled = blink && turnSign < 0f;
        foreach (var r in rightIndicators) r.enabled = blink && turnSign > 0f;

        // Honk at the player if they block the road.
        hornCooldown -= Time.deltaTime;
        if (sys.honkAtPlayer && blockedByPlayer && speed < 0.5f && blockedTimer > 1.2f && hornCooldown <= 0f)
        {
            if (horn == null)
            {
                horn = gameObject.AddComponent<AudioSource>();
                horn.spatialBlend = 1f;
                horn.minDistance = 6f;
                horn.maxDistance = 80f;
                horn.playOnAwake = false;
            }
            horn.pitch = Random.Range(0.85f, 1.15f);
            horn.PlayOneShot(TrafficSystem.HornClip);
            hornCooldown = Random.Range(3f, 6f);
        }
    }

    // ---------------------------------------------------------------- sensing
    private void Sense()
    {
        Vector3 r = Right(heading);
        Vector3 probe = pos + heading * (halfLength * 0.3f);
        curbDist = ScanEdge(probe, r, sys.maxCurbScan);
        // The left side is only needed for turn decisions, so scan it less often (saves raycasts).
        if (senseCount++ % 5 == 0 || curbDist < 0f)
        {
            leftDist = ScanEdge(probe, -r, sys.maxCurbScan * 1.5f);
            if (curbDist > 0f && leftDist > 0f) roadWidth = curbDist + leftDist;
        }
        aheadDist = ScanEdge(pos + heading * halfLength, heading, sys.lookAhead);
        if (aheadDist > 0f) aheadDist += halfLength;

        // Next traffic light on our road.
        lightAhead = null;
        if (sys.trafficLights && (state != State.Turning || turnDelay > 0f))
        {
            var ti = sys.IntersectionAhead(pos, heading, 70f, out float d);
            if (ti != null)
            {
                lightAhead = ti;
                stopLineDist = d - halfLength;
            }
        }
    }

    /// Distance from origin along dir to where the road ends (-1 if it doesn't end within max).
    /// Needs two consecutive off-road samples so small gaps between road tiles are ignored.
    private float ScanEdge(Vector3 origin, Vector3 dir, float max)
    {
        const float step = 0.5f;
        bool prevOff = false;
        for (float d = step; d <= max; d += step)
        {
            bool off = !sys.IsRoad(origin + dir * d);
            if (off && prevOff) return d - step;
            prevOff = off;
        }
        return -1f;
    }

    // ---------------------------------------------------------------- decisions
    private void Decide()
    {
        switch (state)
        {
            case State.Cruise:
                if (sinceManeuver < 6f) break;                       // just left a junction
                if (curbDist < 0f) AtJunction();                     // right curb ended: side road
                else if (aheadDist > 0f) AtRoadEnd();                // road ends/bends ahead
                break;

            case State.Crossing:
                if (curbDist > 0f && sinceManeuver > 4f) { state = State.Cruise; }
                else if (aheadDist > 0f && aheadDist < halfLength + 4f) AtRoadEnd();
                break;
        }
    }

    private void AtJunction()
    {
        Vector3 r = Right(heading);
        float lane = sys.laneOffsetFromCurb;

        // Width of the crossing road, measured just to the right of our lane.
        float crossWidth = ScanEdge(pos + r * (lane + 1.5f), heading, 30f);
        if (crossWidth < 0f) crossWidth = roadWidth;

        bool canStraight = sys.IsRoad(pos + heading * (crossWidth + 6f)) && sys.IsRoad(pos + heading * (crossWidth + 12f));
        float leftReach = (leftDist > 0f ? leftDist : roadWidth - lane) + 6f;
        bool canLeft = sys.IsRoad(pos + heading * (crossWidth * 0.5f) - r * leftReach);

        float roll = Random.value;
        if (canStraight && (roll < sys.chanceStraight || (!canLeft && roll < 0.65f)))
        {
            state = State.Crossing;
            sinceManeuver = 0f;
        }
        else if (canLeft && (Random.value < 0.5f))
        {
            float radius = sys.turnRadius;
            StartTurn(-1f, 90f, radius, Mathf.Max(0f, crossWidth - lane - radius));
        }
        else
        {
            // Right turn: circle around the corner at lane distance so we end up in the new right lane.
            float radius = Mathf.Max(sys.minTurnRadius, lane);
            StartTurn(+1f, 90f, radius, 0f);
        }
    }

    private void AtRoadEnd()
    {
        Vector3 r = Right(heading);
        leftDist = ScanEdge(pos + heading * (halfLength * 0.3f), -r, sys.maxCurbScan * 1.5f);
        float lane = sys.laneOffsetFromCurb;
        float leftReach = (leftDist > 0f ? leftDist : roadWidth - lane) + 5f;
        bool canLeft = sys.IsRoad(pos + heading * Mathf.Max(aheadDist - lane, 0f) - r * leftReach);

        if (canLeft)
        {
            float radius = sys.turnRadius;
            StartTurn(-1f, 90f, radius, Mathf.Max(0f, aheadDist - lane - radius));
        }
        else
        {
            // Dead end: U-turn across the road.
            float radius = Mathf.Max(sys.minTurnRadius * 0.8f, (roadWidth - 2f * lane) * 0.5f);
            StartTurn(-1f, 180f, radius, 0f);
        }
    }

    private void StartTurn(float sign, float angle, float radius, float delay)
    {
        turnSign = sign;
        turnRemaining = angle;
        currentTurnRadius = Mathf.Max(radius, 1f);
        turnDelay = delay;
        nextHeading = Quaternion.Euler(0f, sign * angle, 0f) * heading;
        nextHeading = new Vector3(Mathf.Round(nextHeading.x), 0f, Mathf.Round(nextHeading.z));
        state = State.Turning;
    }

    // ---------------------------------------------------------------- steering & speed
    private void Steer(float dt)
    {
        float maxYawRate = speed / Mathf.Max(sys.minTurnRadius, 1f) * Mathf.Rad2Deg;

        if (state == State.Turning)
        {
            if (turnDelay > 0f)
            {
                turnDelay -= speed * dt;
                yaw = Mathf.MoveTowardsAngle(yaw, HeadingYaw(heading), maxYawRate * dt);
                return;
            }

            float rate = speed / currentTurnRadius * Mathf.Rad2Deg;
            float d = Mathf.Min(rate * dt, turnRemaining);
            yaw += turnSign * d;
            turnRemaining -= d;
            if (turnRemaining <= 0.01f)
            {
                heading = nextHeading;
                yaw = HeadingYaw(heading);
                state = State.Cruise;
                sinceManeuver = 0f;
                curbDist = -1f;
                senseTimer = 0f;   // re-sense right away
            }
            return;
        }

        float target = HeadingYaw(heading);
        if (state == State.Cruise && curbDist > 0f)
        {
            // Hold the lane: steer toward the right curb if too far from it, away if too close.
            float error = curbDist - sys.laneOffsetFromCurb;
            target += Mathf.Clamp(error * 6f, -20f, 20f);
        }
        yaw = Mathf.MoveTowardsAngle(yaw, target, maxYawRate * dt);
    }

    private void UpdateSpeed(float dt)
    {
        // Desired speed: slower through corners and junctions.
        float desired = sys.cruiseSpeed * desiredSpeedFactor;
        if (state == State.Turning || (state == State.Cruise && (curbDist < 0f || aheadDist > 0f)))
            desired = Mathf.Min(desired, sys.turnSpeed * desiredSpeedFactor);

        // Free-road acceleration (IDM).
        float a = maxAccel * (1f - Mathf.Pow(speed / Mathf.Max(desired, 0.1f), 4f));

        // Car / person / object in front.
        blockedByPlayer = false;
        TrafficCar leaderCar = null;
        if (ignoreObstaclesTimer > 0f)
        {
            ignoreObstaclesTimer -= dt;
        }
        else
        {
            float gap = DistanceToObstacle(out float leaderSpeed, out bool isPlayer, out leaderCar);
            if (gap >= 0f)
            {
                a = Mathf.Min(a, FollowAccel(gap, leaderSpeed, desired));
                blockedByPlayer = isPlayer;
            }
        }

        QueuedBehindPlayer = blockedByPlayer || (leaderCar != null && leaderCar.QueuedBehindPlayer);

        // Traffic light: treat the stop line as a stopped car.
        bool redLight = MustStopAtLight();
        // Don't enter a junction we can't get out of (it would lock the cross traffic).
        bool junctionHold = !redLight && JunctionBlockedAhead(dt);
        if (redLight || junctionHold)
            a = Mathf.Min(a, FollowAccel(Mathf.Max(stopLineDist, 0.01f), 0f, desired));
        WaitingAtLight = speed < 1f && (redLight || junctionHold || (leaderCar != null && leaderCar.WaitingAtLight));

        a = Mathf.Clamp(a, -sys.braking, maxAccel);

        // Stuck for a long time behind something that isn't moving: try to squeeze past.
        // (The hard outline check in FixedUpdate still stops it from driving into a car.)
        if (speed < 0.2f && a <= 0.05f && !WaitingAtLight)
        {
            blockedTimer += dt;
            if (blockedTimer > sys.patience && !blockedByPlayer && !QueuedBehindPlayer)
            {
                ignoreObstaclesTimer = 1.5f;
                blockedTimer = 0f;
            }
        }
        else if (!blockedByPlayer) blockedTimer = 0f;
        else blockedTimer += dt;

        accel = a;
        speed = Mathf.Max(0f, speed + a * dt);
    }

    /// Intelligent Driver Model interaction term: acceleration when following something 'gap' metres ahead.
    private float FollowAccel(float gap, float leaderSpeed, float desired)
    {
        float dv = speed - leaderSpeed;
        float sStar = minGap + Mathf.Max(0f, speed * headway + speed * dv / (2f * Mathf.Sqrt(maxAccel * sys.comfortableBraking)));
        float free = maxAccel * (1f - Mathf.Pow(speed / Mathf.Max(desired, 0.1f), 4f));
        return free - maxAccel * (sStar / Mathf.Max(gap, 0.01f)) * (sStar / Mathf.Max(gap, 0.01f));
    }

    private bool MustStopAtLight()
    {
        if (lightAhead == null || (state == State.Turning && turnDelay <= 0f)) return false;
        if (stopLineDist < -0.5f) return false;                       // already past the line: keep going

        LightState light = lightAhead.GetState(Mathf.Abs(heading.z) > 0.5f);
        if (light == LightState.Green) { goingThroughOnYellow = null; return false; }
        if (light == LightState.Yellow)
        {
            if (goingThroughOnYellow == lightAhead) return false;
            // Too close to stop comfortably? Then go through.
            float stoppingDistance = speed * speed / (2f * sys.comfortableBraking);
            if (stopLineDist < stoppingDistance) { goingThroughOnYellow = lightAhead; return false; }
        }
        return true;
    }

    /// Gap from the front bumper to the nearest obstacle in the driving direction (-1 if clear),
    /// and how fast that obstacle is moving in our direction.
    private float DistanceToObstacle(out float leaderSpeed, out bool isPlayer, out TrafficCar leaderCar)
    {
        leaderSpeed = 0f;
        isPlayer = false;
        leaderCar = null;
        Vector3 dir = Direction;
        float look = halfLength + minGap + speed * headway + speed * speed / (2f * sys.comfortableBraking) + 6f;
        Vector3 center = pos + Vector3.up * Mathf.Clamp(height * 0.5f, 0.6f, 1.2f);
        Vector3 halfExtents = new Vector3(halfWidth * 0.95f, 0.4f, 0.1f);

        int n = Physics.BoxCastNonAlloc(center, halfExtents, dir, obstacleHits,
            Quaternion.LookRotation(dir), look, ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        Collider nearestCol = null;
        for (int i = 0; i < n; i++)
        {
            var h = obstacleHits[i];
            if (h.collider.transform.IsChildOf(transform)) continue;   // ourselves
            if (IsGhostPartner(h.collider.attachedRigidbody)) continue;  // passing a stuck car
            float d = h.distance;
            if (d <= 0f)
            {
                // Started overlapping: only counts if it's in front of us.
                Vector3 to = h.collider.bounds.center - pos;
                if (Vector3.Dot(to, dir) <= 0f) continue;
                d = halfLength;
            }
            if (d < nearest) { nearest = d; nearestCol = h.collider; }
        }
        if (nearestCol == null) return -1f;

        var body = nearestCol.attachedRigidbody;
        if (body != null)
        {
            var other = body.GetComponent<TrafficCar>();
            leaderCar = other;
            if (other != null) leaderSpeed = Mathf.Max(0f, other.Speed * Vector3.Dot(other.Direction, dir));
            else
            {
                leaderSpeed = Mathf.Max(0f, Vector3.Dot(body.linearVelocity, dir));
                isPlayer = body.GetComponent<PlayerController>() != null ||
                           body.GetComponent<PlayerVehicle>() != null;
            }
        }
        return Mathf.Max(0f, nearest - halfLength);
    }

    // ---------------------------------------------------------------- car-to-car overlap

    /// Would this car's outline (seen from above, plus a small margin) overlap another car if it moved to nextPos?
    /// Moves that separate two cars which already overlap (e.g. spawned too close) are allowed.
    private bool WouldOverlapCar(Vector3 nextPos, Quaternion nextRot, out TrafficCar blocker)
    {
        blocker = null;
        Footprint(nextPos, nextRot, out Vector3 nc, out Vector3 nf, out Vector3 nr);
        Footprint(rb.position, rb.rotation, out Vector3 cc, out Vector3 cf, out Vector3 cr);

        foreach (var other in AllCars)
        {
            if (other == this || other.rb == null || !other.isActiveAndEnabled) continue;
            if (other == ghostThrough || other.ghostThrough == this) continue;   // passing a stuck car
            other.Footprint(other.rb.position, other.rb.rotation, out Vector3 oc, out Vector3 of, out Vector3 or);

            float reach = footprintRadius + other.footprintRadius + OverlapMargin * 2f;
            if ((oc - nc).sqrMagnitude > reach * reach) continue;
            if (!Overlaps(nc, nf, nr, halfLength, halfWidth, oc, of, or, other.halfLength, other.halfWidth)) continue;

            // Already overlapping now: allow the move if it takes us further apart.
            if (Overlaps(cc, cf, cr, halfLength, halfWidth, oc, of, or, other.halfLength, other.halfWidth) &&
                Flat(nc - oc).sqrMagnitude > Flat(cc - oc).sqrMagnitude)
                continue;

            blocker = other;
            return true;
        }
        return false;
    }

    /// Centre and flat forward / right axes of the car's outline for a given position and rotation.
    private void Footprint(Vector3 position, Quaternion rotation, out Vector3 center, out Vector3 fwd, out Vector3 right)
    {
        center = position + rotation * footprintOffset;
        fwd = Flat(rotation * Vector3.forward).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        right = Vector3.Cross(Vector3.up, fwd);
    }

    /// Separating-axis test for two rectangles on the ground plane (each grown by the margin).
    private static bool Overlaps(Vector3 ca, Vector3 fa, Vector3 ra, float la, float wa,
                                 Vector3 cb, Vector3 fb, Vector3 rb, float lb, float wb)
    {
        la += OverlapMargin; wa += OverlapMargin; lb += OverlapMargin; wb += OverlapMargin;
        Vector3 d = Flat(cb - ca);
        foreach (Vector3 axis in new[] { fa, ra, fb, rb })
        {
            float projA = la * Mathf.Abs(Vector3.Dot(fa, axis)) + wa * Mathf.Abs(Vector3.Dot(ra, axis));
            float projB = lb * Mathf.Abs(Vector3.Dot(fb, axis)) + wb * Mathf.Abs(Vector3.Dot(rb, axis));
            if (Mathf.Abs(Vector3.Dot(d, axis)) > projA + projB) return false;   // a gap on this axis
        }
        return true;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ---------------------------------------------------------------- anti-gridlock

    /// Hard-blocked by 'blocker' this step. Handles jams the two-car rule above doesn't:
    ///  - a ring of 3+ cars blocking each other: the highest-ID car in the ring backs up;
    ///  - blocked by a car that isn't moving: back up a little, and if that doesn't clear it, drive past it.
    /// Cars waiting at a red light or queued behind the player are left alone.
    private void ResolveJam(TrafficCar blocker, float dt)
    {
        clearRun = 0f;
        jamTimer += dt;
        if (blocker == null || backUpTimer > 0f) return;

        if (hardBlockedTimer > 2.5f && YieldsInBlockRing())
        {
            backUpTimer = 2f;
            hardBlockedTimer = 0f;
            return;
        }

        bool blockerStuck = blocker.speed < 0.2f && !blocker.WaitingAtLight && !blocker.QueuedBehindPlayer;
        if (!blockerStuck) return;

        if (jamTimer > sys.jamPassAfter)
        {
            ghostThrough = blocker;
            ghostTimer = 4f;
            jamTimer = 0f;
            hardBlockedTimer = 0f;
        }
        else if (hardBlockedTimer > sys.jamBackUpTime)
        {
            backUpTimer = 1f;
            hardBlockedTimer = 0f;
        }
    }

    /// Follows the chain "I'm blocked by A, A by B, ..." - true if it comes back to this car
    /// and this car has the highest instance ID in the ring (so exactly one car yields).
    private bool YieldsInBlockRing()
    {
        int id = GetInstanceID();
        TrafficCar c = hardBlocker;
        for (int i = 0; i < 8 && c != null; i++)
        {
            if (c == this) return true;
            if (c.speed > 0.2f || c.GetInstanceID() > id) return false;
            c = c.hardBlocker;
        }
        return false;
    }

    private bool IsGhostPartner(Rigidbody body)
    {
        if (body == null) return false;
        if (ghostThrough != null && body == ghostThrough.rb) return true;
        var other = body.GetComponent<TrafficCar>();
        return other != null && other.ghostThrough == this;
    }

    /// "Don't block the box": before the stop line of a junction, true while a stopped car is inside the junction
    /// or the lane straight across is full. Gives up after TrafficSystem.maxJunctionHold so it can't deadlock.
    private bool JunctionBlockedAhead(float dt)
    {
        if (!sys.dontBlockJunctions || lightAhead == null || (state == State.Turning && turnDelay <= 0f) ||
            stopLineDist < 0f || stopLineDist > 15f)
        {
            junctionHoldTimer = 0f;
            return false;
        }
        if (junctionHoldTimer > sys.maxJunctionHold) return false;

        float half = lightAhead.stopLineDistance;
        Vector3 c = lightAhead.Center;
        Vector3 r = Right(heading);
        bool blocked = false;
        foreach (var other in AllCars)
        {
            if (other == this || other.rb == null || !other.isActiveAndEnabled || other.speed > 1f) continue;
            Vector3 p = other.rb.position;
            Vector3 rel = p - c;
            if (Mathf.Abs(rel.x) < half - 0.5f && Mathf.Abs(rel.z) < half - 0.5f) { blocked = true; break; }

            // Our exit lane right after the junction, with no room left for us.
            float along = Vector3.Dot(rel, heading);
            float side = Vector3.Dot(p - pos, r);
            if (along > half && along < half + halfLength * 2f + minGap && Mathf.Abs(side) < halfWidth + 1f &&
                Vector3.Dot(other.Direction, heading) > 0.7f)
            {
                blocked = true;
                break;
            }
        }
        if (blocked) junctionHoldTimer += dt;
        else junctionHoldTimer = 0f;
        return blocked;
    }

    // ---------------------------------------------------------------- helpers
    private static Vector3 Right(Vector3 h) => Vector3.Cross(Vector3.up, h);
    private static float HeadingYaw(Vector3 h) => Quaternion.LookRotation(h).eulerAngles.y;

    private Quaternion CarRotation() =>
        Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, roll) *
        Quaternion.Euler(0f, sys.modelFacesPositiveZ ? 0f : 180f, 0f);

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || state == State.Parked) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pos + Vector3.up, pos + Vector3.up + heading * 5f);
        if (curbDist > 0f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pos + Vector3.up, pos + Vector3.up + Right(heading) * curbDist);
        }
        if (lightAhead != null)
        {
            Gizmos.color = MustStopAtLight() ? Color.red : Color.green;
            Gizmos.DrawLine(pos + Vector3.up, lightAhead.Center + Vector3.up);
        }
    }
}
