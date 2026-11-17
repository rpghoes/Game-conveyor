using UnityEngine;

// Flying Scanner. It searches for defective units with a scanning beam (a cone under it that sweeps
// left and right) and has no weapon of its own - it calls the Guards.
//   PATROL     - flies back and forth, the beam sweeps (cyan)
//   SUSPICIOUS - the Robot is in the beam: the beam turns yellow and locks on for a moment.
//                Leave the beam (or break line of sight) in time and the Scanner goes back to patrolling.
//   ALARM      - the beam turns red, the alarm sounds (AlarmSystem.Raise): Guards hunt the Robot and new ones
//                come out of service doors. The Scanner follows the Robot and keeps the beam on it until it
//                loses it for a few seconds.
// One hit of the press arm disables it.
[RequireComponent(typeof(Rigidbody2D), typeof(EnemyHealth))]
public class ScannerAI : MonoBehaviour
{
    enum State { Patrol, Suspicious, Alarm }

    [Header("Patrol")]
    [Tooltip("How far left/right of its start position the Scanner patrols")]
    public float patrolDistance = 4f;
    public float patrolSpeed = 2f;
    public float hoverAmplitude = 0.3f;
    public float hoverSpeed = 2f;

    [Header("Scanning Beam")]
    public SpriteRenderer beam;
    [Tooltip("Beam length in world units")]
    public float beamRange = 9f;
    [Tooltip("Half of the cone opening, degrees")]
    public float beamHalfAngle = 16f;
    [Tooltip("How far the beam swings to each side of straight down, degrees")]
    public float sweepAngle = 35f;
    public float sweepSpeed = 1.2f;
    [Tooltip("Seconds the Robot must stay in the beam before the alarm")]
    public float suspicionTime = 0.6f;
    [Tooltip("The alarm ends after losing the Robot this long")]
    public float loseTime = 3f;
    public Color patrolColor = new Color(0.4f, 0.95f, 1f, 0.22f);
    public Color suspiciousColor = new Color(1f, 0.85f, 0.25f, 0.32f);
    public Color alarmColor = new Color(1f, 0.2f, 0.15f, 0.4f);

    [Header("Alarm Chase")]
    public float chaseSpeed = 3.5f;
    [Tooltip("How high above the Robot it hovers while the alarm is on")]
    public float hoverHeightAbovePlayer = 4f;

    [Header("Ground (blocks line of sight)")]
    public LayerMask groundLayer;

    [Header("Art")]
    [Tooltip("The provided Scanner Drone artwork faces LEFT")]
    public bool artFacesRight = false;

    [Header("Audio")]
    public AudioSource scanSound;
    public AudioSource alarmSound;

    public bool IsAlarmed => state == State.Alarm;

    private Rigidbody2D rb;
    private EnemyHealth health;
    private SpriteRenderer spriteRenderer;
    private Transform player;
    private PlayerHealth playerHealth;
    private Vector2 startPosition;
    private int patrolDirection = 1;
    private State state = State.Patrol;
    private float stateTimer;
    private float lastSeenTime = -100f;
    private float beamAngle;      // degrees from straight down, + = to the right
    private float sweepPhase;

    Vector2 PlayerTarget => (Vector2)player.position + Vector2.up * 0.8f;
    Vector2 BeamOrigin => rb.position + Vector2.down * 0.3f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<EnemyHealth>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        startPosition = rb.position;
        if (groundLayer == 0) groundLayer = LayerMask.GetMask("Ground");
        sweepPhase = Random.value * 10f;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }

        ProgressionSettings.Tier s = health.Scaling;
        patrolSpeed *= s.moveSpeed;
        chaseSpeed *= s.moveSpeed;
    }

    void Update()
    {
        if (health.IsDead)
        {
            if (beam != null) beam.enabled = false;
            return;
        }

        bool inBeam = PlayerInBeam();
        if (inBeam) lastSeenTime = Time.time;
        stateTimer += Time.deltaTime;

        switch (state)
        {
            case State.Patrol:
                sweepPhase += Time.deltaTime * sweepSpeed;
                beamAngle = Mathf.Sin(sweepPhase) * sweepAngle;
                if (inBeam) SetState(State.Suspicious);
                break;

            case State.Suspicious:
                AimBeamAtPlayer(8f);
                if (!inBeam && stateTimer > 0.15f) SetState(State.Patrol);
                else if (stateTimer >= suspicionTime) SetState(State.Alarm);
                break;

            case State.Alarm:
                AimBeamAtPlayer(12f);
                if (inBeam) AlarmSystem.Raise(rb.position); // re-raised only after the alarm cooldown
                if (Time.time - lastSeenTime > loseTime) SetState(State.Patrol);
                break;
        }
        UpdateBeamVisual();
    }

    void SetState(State next)
    {
        state = next;
        stateTimer = 0f;
        switch (next)
        {
            case State.Suspicious:
                if (scanSound != null) scanSound.Play();
                break;
            case State.Alarm:
                if (alarmSound != null) alarmSound.Play();
                DamageNumber.Show(rb.position + Vector2.up * 0.9f, "!", new Color(1f, 0.25f, 0.2f), 1.6f, 0.8f);
                AlarmSystem.Raise(rb.position);
                break;
            case State.Patrol:
                // Continue sweeping from where the beam points now
                sweepPhase = Mathf.Asin(Mathf.Clamp(beamAngle / Mathf.Max(sweepAngle, 0.01f), -1f, 1f));
                break;
        }
    }

    void FixedUpdate()
    {
        if (health.IsDead || health.IsStunned) return;

        Vector2 position = rb.position;
        Vector2 target;
        float speed;
        if (state == State.Alarm && player != null)
        {
            // Follow the Robot from above, keeping it in the beam
            target = (Vector2)player.position + Vector2.up * hoverHeightAbovePlayer;
            speed = chaseSpeed;
        }
        else if (state == State.Suspicious)
        {
            // Hover in place while scanning
            target = position;
            speed = 0f;
        }
        else
        {
            // Fly back to the patrol route, then back and forth with a gentle bob
            if (position.x > startPosition.x + patrolDistance) patrolDirection = -1;
            else if (position.x < startPosition.x - patrolDistance) patrolDirection = 1;
            float bob = Mathf.Sin(Time.time * hoverSpeed) * hoverAmplitude;
            target = new Vector2(position.x + patrolDirection, startPosition.y + bob);
            speed = patrolSpeed;
        }

        Vector2 toTarget = target - position;
        Vector2 desired = toTarget.magnitude > 0.1f ? toTarget.normalized * speed : Vector2.zero;
        rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, desired, 20f * Time.fixedDeltaTime);
        if (Mathf.Abs(desired.x) > 0.1f) Face(desired.x > 0f ? 1 : -1);
    }

    // ---------------- Beam ----------------

    Vector2 BeamDirection => Quaternion.Euler(0f, 0f, beamAngle) * Vector2.down;

    bool PlayerInBeam()
    {
        if (player == null || playerHealth == null || playerHealth.IsDead) return false;
        Vector2 toPlayer = PlayerTarget - BeamOrigin;
        if (toPlayer.magnitude > beamRange) return false;
        if (Vector2.Angle(BeamDirection, toPlayer) > beamHalfAngle) return false;
        // Walls and floors block the beam
        return !Physics2D.Raycast(BeamOrigin, toPlayer.normalized, toPlayer.magnitude, groundLayer);
    }

    void AimBeamAtPlayer(float turnSpeed)
    {
        if (player == null) return;
        Vector2 toPlayer = PlayerTarget - BeamOrigin;
        float wanted = Vector2.SignedAngle(Vector2.down, toPlayer);
        wanted = Mathf.Clamp(wanted, -70f, 70f);
        beamAngle = Mathf.LerpAngle(beamAngle, wanted, turnSpeed * Time.deltaTime);
    }

    void UpdateBeamVisual()
    {
        if (beam == null) return;
        // The beam sprite points DOWN from its pivot (top centre); scale it to the beam size,
        // shortened where a wall/floor stops it
        float length = beamRange;
        RaycastHit2D wall = Physics2D.Raycast(BeamOrigin, BeamDirection, beamRange, groundLayer);
        if (wall.collider != null) length = wall.distance;

        Vector2 spriteSize = beam.sprite != null ? (Vector2)beam.sprite.bounds.size : Vector2.one;
        float width = 2f * length * Mathf.Tan(beamHalfAngle * Mathf.Deg2Rad);
        beam.transform.position = BeamOrigin;
        // The parent flips (scale.x = -1) with the facing direction, which mirrors the child's rotation:
        // multiply by the sign so the beam is drawn exactly where it is aimed
        float parentSign = Mathf.Sign(transform.lossyScale.x);
        beam.transform.rotation = Quaternion.Euler(0f, 0f, beamAngle * parentSign);
        beam.transform.localScale = new Vector3(parentSign * width / spriteSize.x, length / spriteSize.y, 1f);

        Color c = state == State.Alarm ? alarmColor : state == State.Suspicious ? suspiciousColor : patrolColor;
        // A slow pulse so the beam feels alive; fast flicker during the alarm
        float pulse = state == State.Alarm ? (Mathf.FloorToInt(Time.time * 10f) % 2 == 0 ? 1f : 0.7f) : 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);
        beam.color = new Color(c.r, c.g, c.b, c.a * pulse);
        if (spriteRenderer != null && !health.IsStunned)
            spriteRenderer.color = state == State.Alarm ? Color.Lerp(Color.white, new Color(1f, 0.5f, 0.5f), pulse - 0.7f) : Color.white;
    }

    void Face(int direction)
    {
        Vector3 scale = transform.localScale;
        float size = Mathf.Abs(scale.x);
        scale.x = ((direction > 0) == artFacesRight) ? size : -size;
        transform.localScale = scale;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 start = Application.isPlaying ? (Vector3)startPosition : transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(start + Vector3.left * patrolDistance, start + Vector3.right * patrolDistance);
        Gizmos.color = Color.yellow;
        Vector3 origin = transform.position + Vector3.down * 0.3f;
        Gizmos.DrawLine(origin, origin + Quaternion.Euler(0, 0, sweepAngle) * Vector3.down * beamRange);
        Gizmos.DrawLine(origin, origin + Quaternion.Euler(0, 0, -sweepAngle) * Vector3.down * beamRange);
    }
}
