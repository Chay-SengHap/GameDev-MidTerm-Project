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
    }

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

        // Traffic light: treat the stop line as a stopped car.
        bool redLight = MustStopAtLight();
        if (redLight)
            a = Mathf.Min(a, FollowAccel(Mathf.Max(stopLineDist, 0.01f), 0f, desired));
        WaitingAtLight = speed < 1f && (redLight || (leaderCar != null && leaderCar.WaitingAtLight));

        a = Mathf.Clamp(a, -sys.braking, maxAccel);

        // Stuck for a long time (e.g. two cars blocking each other): squeeze past.
        if (speed < 0.2f && a <= 0.05f && !WaitingAtLight)
        {
            blockedTimer += dt;
            if (blockedTimer > sys.patience && !blockedByPlayer)
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
        Vector3 halfExtents = new Vector3(halfWidth * 0.8f, 0.4f, 0.1f);

        int n = Physics.BoxCastNonAlloc(center, halfExtents, dir, obstacleHits,
            Quaternion.LookRotation(dir), look, ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        Collider nearestCol = null;
        for (int i = 0; i < n; i++)
        {
            var h = obstacleHits[i];
            if (h.collider.transform.IsChildOf(transform)) continue;   // ourselves
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
                           body.GetComponent<PlayerVehicleController>() != null;
            }
        }
        return Mathf.Max(0f, nearest - halfLength);
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
