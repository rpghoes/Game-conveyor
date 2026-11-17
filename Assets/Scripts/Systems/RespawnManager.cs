using UnityEngine;

// Remembers where the Robot respawns in this room.
// Starts at the room's PlayerSpawn and moves to each checkpoint the player touches.
public class RespawnManager : MonoBehaviour
{
    public static RespawnManager Instance { get; private set; }

    [Tooltip("Where the Robot starts in this room (used until a checkpoint is touched)")]
    public Transform playerSpawn;

    public Vector3 RespawnPoint { get; private set; }

    void Awake()
    {
        Instance = this;
        RespawnPoint = playerSpawn != null ? playerSpawn.position : Vector3.zero;
    }

    public void SetCheckpoint(Vector3 position)
    {
        RespawnPoint = position;
    }
}
