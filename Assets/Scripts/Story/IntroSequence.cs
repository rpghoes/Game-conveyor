using System.Collections;
using UnityEngine;

// Opening of Section 01. Unit 0412 lies on the transport belt with other defective units,
// riding towards the furnace. The belt jams - the Robot stops, looks around (it is not supposed to do that)
// and the player gets control. A few seconds later the line restarts, still heading for the furnace.
public class IntroSequence : MonoBehaviour
{
    [Tooltip("The transport belt the Robot starts on (moves towards the furnace)")]
    public ConveyorBelt belt;
    [Tooltip("Other defective units riding the belt ahead of the Robot")]
    public Transform[] cargo;
    [Tooltip("World X of the furnace mouth: cargo that reaches it is recycled")]
    public float furnaceX = 4f;

    [Header("Timing")]
    public float carryTime = 3f;
    public float restartDelay = 9f;

    [Header("Section text (shown when the player gets control)")]
    public string sectionTitle;
    [TextArea] public string controlsHint;

    private PlayerController controller;
    private PlayerHealth health;
    private float beltSpeed;

    IEnumerator Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null || belt == null) yield break;
        controller = player.GetComponent<PlayerController>();
        health = player.GetComponent<PlayerHealth>();
        beltSpeed = belt.speed;

        controller.canControl = false;
        health.invulnerable = true;
        controller.LookTowards(beltSpeed > 0f);
        yield return null;

        UIManager ui = UIManager.Instance;
        if (ui != null)
        {
            ui.ShowBanner("QUALITY CONTROL", 2.8f);
            ui.ShowMessage("UNIT 0412: DEFECTIVE.  ROUTE: FURNACE.", 3f);
        }

        // Ride the belt
        yield return new WaitForSeconds(carryTime);

        // The line jams
        belt.SetSpeed(0f, false);
        CameraFollow.Shake(0.12f, 0.3f);
        Fx.Sparks(belt.transform.position + Vector3.up * 0.5f, new Color(1f, 0.8f, 0.4f), 12, 5f);
        if (ui != null) ui.ShowMessage("CONVEYOR LINE 3: FAULT.  RESTARTING...", 3f);
        yield return new WaitForSeconds(0.9f);

        // Unit 0412 does something a robot should not do: it looks around
        bool towardsFurnace = beltSpeed > 0f;
        controller.LookTowards(!towardsFurnace);
        yield return new WaitForSeconds(0.7f);
        controller.LookTowards(towardsFurnace);
        yield return new WaitForSeconds(0.5f);
        controller.LookTowards(!towardsFurnace);
        yield return new WaitForSeconds(0.4f);

        controller.canControl = true;
        health.invulnerable = false;
        if (ui != null)
        {
            if (!string.IsNullOrEmpty(sectionTitle)) ui.ShowBanner(sectionTitle, 2.5f);
            if (!string.IsNullOrEmpty(controlsHint)) ui.ShowMessage(controlsHint, 6f);
        }

        // The factory does not notice anything unusual and restarts the line
        yield return new WaitForSeconds(restartDelay);
        if (ui != null) ui.ShowMessage("CONVEYOR LINE 3: RESTARTED.", 2.5f);
        belt.SetSpeed(beltSpeed);
    }

    void Update()
    {
        if (belt == null || cargo == null) return;
        // The furnace is where the line normally runs (the sign of the belt's starting speed),
        // not where it happens to move right now - a jammed belt (speed 0) must not "recycle" anything
        float towardsFurnace = Mathf.Sign(beltSpeed != 0f ? beltSpeed : belt.speed);
        foreach (Transform unit in cargo)
        {
            if (unit == null || !unit.gameObject.activeSelf) continue;
            unit.position += Vector3.right * belt.CurrentSpeed * Time.deltaTime;
            bool reachedFurnace = towardsFurnace < 0f ? unit.position.x <= furnaceX : unit.position.x >= furnaceX;
            if (!reachedFurnace) continue;
            // Recycled
            Vector2 at = (Vector2)unit.position + Vector2.up * 0.6f;
            Fx.Explosion(at, new Color(1f, 0.55f, 0.2f), 0.7f);
            Fx.Sparks(at, new Color(1f, 0.8f, 0.4f), 10, 5f);
            unit.gameObject.SetActive(false);
        }
    }
}
