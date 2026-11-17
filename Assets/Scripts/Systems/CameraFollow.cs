using UnityEngine;

// Simple smooth 2D camera that follows the Robot and never shows
// anything outside the room bounds. Also does screen shake (CameraFollow.Shake).
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    public Transform target;
    [Tooltip("Lower = snappier, higher = smoother")]
    public float smoothTime = 0.15f;
    [Tooltip("Keeps the Robot a little below the centre of the screen")]
    public Vector2 offset = new Vector2(0f, 1.5f);

    [Header("Room Bounds (world units)")]
    public bool useBounds = true;
    public Vector2 minBounds = Vector2.zero;
    public Vector2 maxBounds = new Vector2(50f, 20f);

    private Camera cam;
    private Vector3 velocity;
    private Vector3 followPosition;   // camera position without shake
    private float shakeStrength;
    private float shakeTime;
    private float shakeDuration;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        cam = GetComponent<Camera>();
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }
        if (target != null) transform.position = ClampToBounds(DesiredPosition());
        followPosition = transform.position;
    }

    // strength = max offset in world units. A stronger shake replaces a weaker one.
    public static void Shake(float strength, float duration)
    {
        if (Instance == null) return;
        float current = Instance.shakeDuration > 0f ? Instance.shakeStrength * (Instance.shakeTime / Instance.shakeDuration) : 0f;
        if (strength < current) return;
        Instance.shakeStrength = strength;
        Instance.shakeTime = duration;
        Instance.shakeDuration = duration;
    }

    void LateUpdate()
    {
        if (target != null)
        {
            Vector3 smoothed = Vector3.SmoothDamp(followPosition, DesiredPosition(), ref velocity, smoothTime);
            followPosition = ClampToBounds(smoothed);
        }

        Vector3 shake = Vector3.zero;
        if (shakeTime > 0f)
        {
            shakeTime -= Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01(shakeTime / shakeDuration);
            shake = (Vector3)(Random.insideUnitCircle * shakeStrength * fade);
        }

        transform.position = followPosition + shake;
        transform.rotation = Quaternion.identity; // never rotate
    }

    Vector3 DesiredPosition()
    {
        return new Vector3(target.position.x + offset.x, target.position.y + offset.y, -10f);
    }

    Vector3 ClampToBounds(Vector3 position)
    {
        if (!useBounds) return position;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        // If the room is smaller than the screen, just centre on it
        position.x = (maxBounds.x - minBounds.x) > halfWidth * 2f
            ? Mathf.Clamp(position.x, minBounds.x + halfWidth, maxBounds.x - halfWidth)
            : (minBounds.x + maxBounds.x) * 0.5f;
        position.y = (maxBounds.y - minBounds.y) > halfHeight * 2f
            ? Mathf.Clamp(position.y, minBounds.y + halfHeight, maxBounds.y - halfHeight)
            : (minBounds.y + maxBounds.y) * 0.5f;
        position.z = -10f;
        return position;
    }

    void OnDrawGizmosSelected()
    {
        if (!useBounds) return;
        Gizmos.color = Color.cyan;
        Vector3 center = (minBounds + maxBounds) * 0.5f;
        Gizmos.DrawWireCube(center, maxBounds - minBounds);
    }
}
