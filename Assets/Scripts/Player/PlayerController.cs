using UnityEngine;
using UnityEngine.InputSystem;

// Moves the Robot: running, jumping, double jumping and turning around.
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

    [Header("Ground Check")]
    public Transform groundCheck;
    public Vector2 groundCheckSize = new Vector2(0.6f, 0.12f);
    public LayerMask groundLayer;

    [Header("Art")]
    [Tooltip("The provided Robot artwork faces LEFT, so this is off")]
    public bool artFacesRight = false;

    [Header("Audio")]
    public AudioSource jumpSound;

    // PlayerHealth turns this off while the Robot is dead
    [HideInInspector] public bool canControl = true;
    // PlayerUltimate turns this on: gravity and steering are off, the Robot just drifts at frozenVelocity
    [HideInInspector] public bool frozen;
    [HideInInspector] public Vector2 frozenVelocity;

    public bool IsGrounded { get; private set; }
    public bool FacingRight { get; private set; } = true;
    public float FacingDirection => FacingRight ? 1f : -1f;

    private Rigidbody2D rb;
    private Animator animator;
    private float moveInput;
    private bool jumpHeld;
    private int jumpsUsed;
    private float lastGroundedTime = -10f;
    private float knockbackTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        rb.gravityScale = gravityScale;
        SetFacing(true);
    }

    void Update()
    {
        CheckGround();

        Keyboard keyboard = Keyboard.current;
        if (!canControl || keyboard == null)
        {
            moveInput = 0f;
            jumpHeld = false;
            UpdateAnimator();
            return;
        }

        // --- Horizontal input ---
        moveInput = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) moveInput -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) moveInput += 1f;

        // --- Jump ---
        jumpHeld = keyboard.spaceKey.isPressed;
        if (keyboard.spaceKey.wasPressedThisFrame) TryJump();

        // --- Turn around ---
        if (moveInput > 0f && !FacingRight) SetFacing(true);
        else if (moveInput < 0f && FacingRight) SetFacing(false);

        UpdateAnimator();
    }

    void FixedUpdate()
    {
        if (frozen)
        {
            rb.gravityScale = 0f;
            rb.linearVelocity = frozenVelocity;
            return;
        }

        Vector2 velocity = rb.linearVelocity;

        if (knockbackTimer > 0f)
        {
            // While knocked back the player can't steer
            knockbackTimer -= Time.fixedDeltaTime;
        }
        else
        {
            // Smoothly speed up / slow down towards the target speed
            // Levels make the Robot a little faster
            float targetSpeed = moveInput * moveSpeed * PlayerProgression.PlayerMoveSpeedMultiplier;
            float rate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
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
        bool touchingGround = Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundLayer);

        // Only count as grounded when not moving up, so passing up through
        // a one-way platform doesn't refill the jumps
        IsGrounded = touchingGround && rb.linearVelocity.y <= 0.1f;

        if (IsGrounded)
        {
            jumpsUsed = 0;
            lastGroundedTime = Time.time;
        }
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
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, force);
        IsGrounded = false;
        lastGroundedTime = -10f; // no coyote jump after a real jump
        if (jumpSound != null) jumpSound.Play();
    }

    // Called by PlayerHealth when the Robot gets hit
    public void ApplyKnockback(Vector2 velocity, float controlLockTime)
    {
        rb.linearVelocity = velocity;
        knockbackTimer = controlLockTime;
    }

    public void StopMovement()
    {
        moveInput = 0f;
        knockbackTimer = 0f;
        rb.linearVelocity = Vector2.zero;
    }

    void SetFacing(bool faceRight)
    {
        FacingRight = faceRight;

        // Flipping the scale also moves child objects (like the attack hitbox) to the other side
        Vector3 scale = transform.localScale;
        float size = Mathf.Abs(scale.x);
        scale.x = (faceRight == artFacesRight) ? size : -size;
        transform.localScale = scale;
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
