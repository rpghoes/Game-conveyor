using UnityEngine;

// Moves Unit 0412: running, jumping, double jumping, dashing and turning around.
// Standing on a ConveyorBelt adds the belt speed. Input comes from GameInput (keyboard / gamepad).
// Personality: a small squash when landing (dust), and when left alone it stops and looks around.
// Also sends movement values to the Animator (Speed, Grounded, VerticalVelocity).
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 9f;
    [Tooltip("How quickly the Robot reaches full speed")]
    public float acceleration = 90f;
    [Tooltip("How quickly the Robot stops when no key is held")]
    public float deceleration = 120f;
    [Range(0f, 1f)] public float airControl = 0.8f;

    [Header("Jumping")]
    public float jumpForce = 18f;
    public float doubleJumpForce = 15f;
    [Tooltip("1 = ground jump only, 2 = ground jump + one air jump")]
    public int maxJumps = 2;
    [Tooltip("Short grace period to still jump right after walking off a ledge")]
    public float coyoteTime = 0.1f;

    [Header("Gravity")]
    public float gravityScale = 4f;
    [Tooltip("Extra gravity while falling, makes jumps feel less floaty")]
    public float fallGravityMultiplier = 1.6f;
    [Tooltip("Extra gravity when Space is released early, gives short hops")]
    public float shortHopGravityMultiplier = 2f;
    public float maxFallSpeed = 22f;

    [Header("Dash")]
    public float dashSpeed = 19f;
    public float dashTime = 0.14f;
    public float dashCooldown = 0.75f;
    [Tooltip("Dashes allowed before touching the ground again")]
    public int airDashes = 1;
    public Color dashTrailColor = new Color(0.45f, 0.95f, 1f, 0.55f);

    [Header("Personality")]
    [Tooltip("Standing still this long makes the Robot look around")]
    public float idleLookDelay = 4f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public Vector2 groundCheckSize = new Vector2(0.6f, 0.12f);
    public LayerMask groundLayer;

    [Header("Art")]
    [Tooltip("The provided Robot artwork faces LEFT, so this is off")]
    public bool artFacesRight = false;

    [Header("Audio")]
    public AudioSource jumpSound;
    public AudioSource dashSound;

    // PlayerHealth turns this off while the Robot is dead; cutscenes turn it off too
    [HideInInspector] public bool canControl = true;

    public bool IsGrounded { get; private set; }
    public bool FacingRight { get; private set; } = true;
    public float FacingDirection => FacingRight ? 1f : -1f;
    // Speed of the conveyor belt under the Robot's feet (0 when not on a belt)
    public float BeltSpeed { get; private set; }
    public bool IsDashing => dashTimer > 0f;
    // 0 = just used, 1 = ready (for the HUD)
    public float DashReady => CurrentDashCooldown <= 0f ? 1f : Mathf.Clamp01(1f - (nextDashTime - Time.time) / CurrentDashCooldown);
    float CurrentDashCooldown => dashCooldown * PlayerUpgrades.DashCooldownMultiplier;

    private Rigidbody2D rb;
    private Animator animator;
    private float moveInput;
    private bool jumpHeld;
    private int jumpsUsed;
    private float lastGroundedTime = -10f;
    private float knockbackTimer;
    private float dashTimer;
    private float dashDirection;
    private float nextDashTime;
    private int dashesUsed;
    private float trailTimer;
    private bool wasGrounded = true;
    private float idleTime;
    private float nextLookTime;
    private SpriteRenderer spriteRenderer;
    private Vector3 baseScale;
    private float squash;          // > 0 = squashed (landing), < 0 = stretched (jump)

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, transform.localScale.z);
        rb.gravityScale = gravityScale;
        SetFacing(true);
    }

    void Update()
    {
        CheckGround();

        if (!canControl)
        {
            moveInput = 0f;
            jumpHeld = false;
            dashTimer = 0f;
            idleTime = 0f;
            UpdateAnimator();
            UpdateSquash();
            return;
        }

        // --- Horizontal input ---
        moveInput = GameInput.Horizontal;

        // --- Jump ---
        jumpHeld = GameInput.JumpHeld;
        if (GameInput.JumpPressed) TryJump();

        // --- Dash ---
        if (GameInput.DashPressed) TryDash();

        // --- Turn around ---
        if (!IsDashing)
        {
            if (moveInput > 0f && !FacingRight) SetFacing(true);
            else if (moveInput < 0f && FacingRight) SetFacing(false);
        }

        LookAroundWhenIdle();
        UpdateAnimator();
        UpdateSquash();
    }

    // ---------------- Dash ----------------

    void TryDash()
    {
        if (IsDashing || knockbackTimer > 0f || Time.time < nextDashTime) return;
        if (!IsGrounded && dashesUsed >= airDashes) return;
        dashDirection = Mathf.Abs(moveInput) > 0.01f ? Mathf.Sign(moveInput) : FacingDirection;
        SetFacing(dashDirection > 0f);
        dashTimer = dashTime;
        nextDashTime = Time.time + dashTime + CurrentDashCooldown;
        if (!IsGrounded) dashesUsed++;
        trailTimer = 0f;
        squash = -0.25f;
        if (dashSound != null) dashSound.Play();
        Fx.Sparks((Vector2)transform.position + new Vector2(-dashDirection * 0.3f, 0.3f), new Color(0.8f, 0.8f, 0.85f), 5, 3f);
    }

    // ---------------- Personality ----------------

    // Standing still for a while, Unit 0412 glances around: it is not supposed to be curious
    void LookAroundWhenIdle()
    {
        bool still = IsGrounded && Mathf.Abs(moveInput) < 0.01f && Mathf.Abs(rb.linearVelocity.x - BeltSpeed) < 0.2f;
        if (!still)
        {
            idleTime = 0f;
            return;
        }
        idleTime += Time.deltaTime;
        if (idleTime < idleLookDelay) return;
        if (Time.time >= nextLookTime)
        {
            SetFacing(!FacingRight);
            nextLookTime = Time.time + Random.Range(0.9f, 2.2f);
        }
    }

    // Squash and stretch on top of the facing scale (the Robot art has no jump/land frames yet)
    void UpdateSquash()
    {
        squash = Mathf.MoveTowards(squash, 0f, Time.deltaTime * 2.5f);
        float sx = 1f + squash * 0.6f;
        float sy = 1f - squash;
        transform.localScale = new Vector3(Mathf.Sign(transform.localScale.x) * baseScale.x * sx, baseScale.y * sy, baseScale.z);
    }

    void FixedUpdate()
    {
        Vector2 velocity = rb.linearVelocity;

        if (IsDashing)
        {
            // Straight horizontal burst, no gravity; leaves a trail of afterimages
            dashTimer -= Time.fixedDeltaTime;
            rb.gravityScale = 0f;
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
            trailTimer -= Time.fixedDeltaTime;
            if (trailTimer <= 0f)
            {
                trailTimer = 0.035f;
                Fx.Afterimage(spriteRenderer, dashTrailColor, 0.22f);
            }
            if (dashTimer <= 0f) rb.linearVelocity = new Vector2(dashDirection * moveSpeed, 0f);
            return;
        }

        if (knockbackTimer > 0f)
        {
            // While knocked back the player can't steer
            knockbackTimer -= Time.fixedDeltaTime;
        }
        else
        {
            // Smoothly speed up / slow down towards the target speed
            // Levels make the Robot a little faster
            // On a conveyor belt "standing still" means moving with the belt
            float targetSpeed = moveInput * moveSpeed * PlayerProgression.PlayerMoveSpeedMultiplier * PlayerUpgrades.SpeedMultiplier + BeltSpeed;
            float rate = Mathf.Abs(moveInput) > 0.01f ? acceleration : deceleration;
            if (!IsGrounded) rate *= airControl;
            velocity.x = Mathf.MoveTowards(velocity.x, targetSpeed, rate * Time.fixedDeltaTime);
        }

        // Heavier gravity when falling or when Space is released early = snappier jumps
        if (velocity.y < 0f) rb.gravityScale = gravityScale * fallGravityMultiplier;
        else if (velocity.y > 0f && !jumpHeld) rb.gravityScale = gravityScale * shortHopGravityMultiplier;
        else rb.gravityScale = gravityScale;

        velocity.y = Mathf.Max(velocity.y, -maxFallSpeed);
        rb.linearVelocity = velocity;
    }

    void CheckGround()
    {
        Collider2D ground = Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundLayer);

        // Only count as grounded when not moving up, so passing up through
        // a one-way platform doesn't refill the jumps
        IsGrounded = ground != null && rb.linearVelocity.y <= 0.1f;
        BeltSpeed = IsGrounded ? ConveyorBelt.SpeedUnder(ground) : 0f;

        if (IsGrounded)
        {
            jumpsUsed = 0;
            dashesUsed = 0;
            lastGroundedTime = Time.time;
        }

        // Landing: squash + a puff of dust
        if (IsGrounded && !wasGrounded && rb != null)
        {
            squash = 0.22f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 at = (Vector2)transform.position + new Vector2(Random.Range(-0.4f, 0.4f), 0.05f);
                Fx.Spawn(Fx.Circle, at, new Vector2(Random.Range(-1.5f, 1.5f), Random.Range(0.2f, 0.8f)), 0.35f, 0.25f, 0.6f,
                    new Color(0.65f, 0.6f, 0.55f, 0.45f), new Color(0.65f, 0.6f, 0.55f, 0f), 0f, 3f, -25);
            }
        }
        wasGrounded = IsGrounded;
    }

    void TryJump()
    {
        bool canGroundJump = IsGrounded || (jumpsUsed == 0 && Time.time - lastGroundedTime <= coyoteTime);

        if (canGroundJump)
        {
            Jump(jumpForce);
            jumpsUsed = 1;
            return;
        }

        // Walked off a ledge without jumping: the ground jump is gone, only the air jump is left
        if (jumpsUsed == 0) jumpsUsed = 1;

        if (jumpsUsed < maxJumps)
        {
            Jump(doubleJumpForce);
            jumpsUsed++;
            if (animator != null) animator.SetTrigger("DoubleJump");
        }
    }

    void Jump(float force)
    {
        squash = -0.15f;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, force);
        IsGrounded = false;
        lastGroundedTime = -10f; // no coyote jump after a real jump
        if (jumpSound != null) jumpSound.Play();
    }

    // Called by PlayerHealth when the Robot gets hit
    public void ApplyKnockback(Vector2 velocity, float controlLockTime)
    {
        dashTimer = 0f;
        rb.linearVelocity = velocity;
        knockbackTimer = controlLockTime;
    }

    public void StopMovement()
    {
        moveInput = 0f;
        knockbackTimer = 0f;
        dashTimer = 0f;
        rb.linearVelocity = Vector2.zero;
    }

    // Cutscenes turn the Robot to look around
    public void LookTowards(bool right)
    {
        SetFacing(right);
    }

    void SetFacing(bool faceRight)
    {
        FacingRight = faceRight;

        // Flipping the scale also moves child objects (like the attack hitbox) to the other side
        Vector3 scale = transform.localScale;
        float size = Mathf.Abs(scale.x);
        scale.x = (faceRight == artFacesRight) ? size : -size;
        transform.localScale = scale;
        idleTime = Mathf.Min(idleTime, idleLookDelay);
    }

    void UpdateAnimator()
    {
        if (animator == null) return;
        animator.SetFloat("Speed", Mathf.Abs(moveInput));
        animator.SetBool("Grounded", IsGrounded);
        animator.SetFloat("VerticalVelocity", rb.linearVelocity.y);
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
    }
}
