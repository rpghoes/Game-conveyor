using UnityEngine;
using UnityEngine.InputSystem;

// One place that answers "what is the player pressing" for keyboard and gamepad.
// Gameplay scripts read GameInput instead of Keyboard.current, so controls can be changed here
// and automated tests can press buttons with GameInput.Simulate*.
//   Keyboard: A/D or arrows move, Space jump, J attack, E interact
//   Gamepad:  left stick / d-pad move, South (A) jump, West (X) attack, North (Y) interact
public static class GameInput
{
    public enum Action { Jump, Attack, Interact }

    // ---------------- Simulation (tests) ----------------
    // While a simulated axis is set it replaces the real horizontal input.
    private static float? simulatedHorizontal;
    private static bool simulatedJumpHeld;
    private static readonly int[] simulatedPressFrame = { -1, -1, -1 };

    // Press an action on the next frame (as if the button went down once)
    public static void SimulatePress(Action action)
    {
        simulatedPressFrame[(int)action] = Time.frameCount + 1;
        if (action == Action.Jump) simulatedJumpHeld = true;
    }

    public static void SimulateHorizontal(float value) { simulatedHorizontal = Mathf.Clamp(value, -1f, 1f); }
    public static void SimulateJumpHeld(bool held) { simulatedJumpHeld = held; }

    public static void ClearSimulation()
    {
        simulatedHorizontal = null;
        simulatedJumpHeld = false;
        for (int i = 0; i < simulatedPressFrame.Length; i++) simulatedPressFrame[i] = -1;
    }

    // ---------------- Queries ----------------

    // -1 .. 1
    public static float Horizontal
    {
        get
        {
            if (simulatedHorizontal.HasValue) return simulatedHorizontal.Value;
            float x = 0f;
            Keyboard k = Keyboard.current;
            if (k != null)
            {
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) x -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1f;
            }
            Gamepad g = Gamepad.current;
            if (g != null && Mathf.Approximately(x, 0f))
            {
                float stick = g.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > 0.35f) x = Mathf.Sign(stick);
                if (g.dpad.left.isPressed) x = -1f;
                if (g.dpad.right.isPressed) x = 1f;
            }
            return x;
        }
    }

    public static bool JumpHeld
    {
        get
        {
            if (simulatedJumpHeld) return true;
            Keyboard k = Keyboard.current;
            Gamepad g = Gamepad.current;
            return (k != null && k.spaceKey.isPressed) || (g != null && g.buttonSouth.isPressed);
        }
    }

    public static bool JumpPressed => Pressed(Action.Jump);
    public static bool AttackPressed => Pressed(Action.Attack);
    public static bool InteractPressed => Pressed(Action.Interact);

    static bool Pressed(Action action)
    {
        if (simulatedPressFrame[(int)action] == Time.frameCount) return true;
        Keyboard k = Keyboard.current;
        Gamepad g = Gamepad.current;
        switch (action)
        {
            case Action.Jump:
                return (k != null && k.spaceKey.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame);
            case Action.Attack:
                return (k != null && k.jKey.wasPressedThisFrame) || (g != null && g.buttonWest.wasPressedThisFrame);
            case Action.Interact:
                return (k != null && k.eKey.wasPressedThisFrame) || (g != null && g.buttonNorth.wasPressedThisFrame);
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() { ClearSimulation(); }
}
