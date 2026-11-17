using System.Collections;
using UnityEngine;

// Director, phase 1 target: a manipulator built into the control room wall.
// When the Robot comes close it winds up (red flash), then rams a piston along the floor - jump over it.
// The housing is the hit target (EnemyHealth on the same object). The Director switches it on.
[RequireComponent(typeof(EnemyHealth))]
public class WallManipulator : MonoBehaviour
{
    [Tooltip("+1 = the piston shoots to the right (left wall), -1 = to the left (right wall)")]
    public int direction = 1;
    public float reach = 4.5f;
    public float triggerRange = 7f;
    public float windup = 0.8f;
    public float thrustSpeed = 22f;
    public float retractSpeed = 5f;
    public float cooldown = 2.5f;
    [Tooltip("Height of the piston above the floor (world Y of its centre)")]
    public float pistonY = 2.5f;
    public int damage = 1;

    [Header("Art")]
    public SpriteRenderer housing;
    public SpriteRenderer piston;
    public Color windupColor = new Color(1f, 0.3f, 0.25f);

    [Header("Audio")]
    public AudioSource thrustSound;

    [HideInInspector] public bool active;

    private EnemyHealth health;
    private Transform player;
    private PlayerHealth playerHealth;
    private float extended;
    private float nextAttack;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        UpdatePiston();
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        while (!health.IsDead)
        {
            if (!active || player == null || Time.time < nextAttack || Mathf.Abs(player.position.x - transform.position.x) > triggerRange)
            {
                yield return null;
                continue;
            }

            // Wind-up: the housing flashes red and the piston twitches back
            float t = 0f;
            while (t < windup && !health.IsDead)
            {
                t += Time.deltaTime;
                if (housing != null) housing.color = Mathf.FloorToInt(t * 14f) % 2 == 0 ? windupColor : Color.white;
                extended = -0.15f * Mathf.Sin(t / windup * Mathf.PI);
                UpdatePiston();
                yield return null;
            }
            if (housing != null) housing.color = Color.white;
            if (health.IsDead) break;

            // Thrust
            if (thrustSound != null) thrustSound.Play();
            bool hit = false;
            while (extended < reach && !health.IsDead)
            {
                extended = Mathf.Min(extended + thrustSpeed * Time.deltaTime, reach);
                UpdatePiston();
                if (!hit) hit = HitCheck();
                yield return null;
            }
            CameraFollow.Shake(0.1f, 0.15f);
            Fx.Sparks(PistonTip, new Color(1f, 0.8f, 0.4f), 6, 4f);
            yield return new WaitForSeconds(0.35f);

            // Retract
            while (extended > 0f && !health.IsDead)
            {
                extended = Mathf.Max(extended - retractSpeed * Time.deltaTime, 0f);
                UpdatePiston();
                yield return null;
            }
            nextAttack = Time.time + cooldown;
        }
        extended = 0f;
        if (piston != null) piston.enabled = false;
    }

    Vector2 PistonTip => new Vector2(transform.position.x + direction * (0.6f + extended), pistonY);

    bool HitCheck()
    {
        if (playerHealth == null || playerHealth.IsDead) return false;
        Vector2 chest = (Vector2)player.position + Vector2.up * 0.5f;
        float from = transform.position.x + direction * 0.6f;
        float to = PistonTip.x;
        bool inside = chest.x > Mathf.Min(from, to) && chest.x < Mathf.Max(from, to) && Mathf.Abs(chest.y - pistonY) < 0.7f;
        if (inside) playerHealth.TakeDamage(damage, new Vector2(from, chest.y), health.DamageMultiplier);
        return inside;
    }

    void UpdatePiston()
    {
        if (piston == null) return;
        float length = Mathf.Max(extended, 0.05f);
        piston.transform.position = new Vector3(transform.position.x + direction * (0.6f + length * 0.5f), pistonY, 0f);
        piston.size = new Vector2(length, piston.size.y);
    }
}
