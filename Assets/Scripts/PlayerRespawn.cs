using System.Collections;
using SUPERCharacter;
using UnityEngine;

public class PlayerRespawn : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float fallY = -40f;
    [SerializeField] private float respawnDelay = 1f;
    [SerializeField] private bool resetAmmoOnRespawn = true;
    [SerializeField] private bool restoreHealthOnRespawn = true;

    [Header("Ground Snap")]
    [SerializeField] private bool snapToGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float probeHeight = 100f;
    [SerializeField] private float probeDistance = 300f;
    [SerializeField] private float groundOffset = 0.1f;
    [SerializeField] private float settleDelay = 0.1f;

    private SUPERCharacterAIO character;
    private Rigidbody playerBody;
    private SimpleShoot gun;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private bool respawning;
    private bool hasRequestedPoint;
    private WorldChunkGenerator chunkGenerator;
    private PlayerHealth healthController;

    public Vector3 CurrentSpawnPosition => spawnPosition;
    public PlayerHealth HealthController => healthController;

    void Start()
    {
        healthController = GetComponent<PlayerHealth>();

        if (!player)
        {
            SUPERCharacterAIO found = FindFirstObjectByType<SUPERCharacterAIO>();
            if (found)
                player = found.transform;
        }
        else
        {
            character = player.GetComponent<SUPERCharacterAIO>();
        }

        if (!player)
        {
            Debug.LogWarning("PlayerRespawn: no player found.");
            enabled = false;
            return;
        }

        playerBody = player.GetComponent<Rigidbody>();
        gun = player.GetComponentInChildren<SimpleShoot>();
        chunkGenerator = FindFirstObjectByType<WorldChunkGenerator>();

        if (spawnPoint)
        {
            spawnPosition = spawnPoint.position;
            spawnRotation = spawnPoint.rotation;
        }
        else
        {
            spawnPosition = player.position;
            spawnRotation = player.rotation;
        }

        if (!IsLocalPlayer())
            return;

        if (snapToGround)
            StartCoroutine(SettleOnGroundRoutine());
    }

    void Update()
    {
        if (respawning || !player || healthController)
            return;

        if (!IsLocalPlayer())
            return;

        bool shouldRespawn = player.position.y < fallY;

        if (!shouldRespawn && character && character.enableSurvivalStats && character.currentSurvivalStats.Health <= 0f)
            shouldRespawn = true;

        if (shouldRespawn)
            StartCoroutine(RespawnRoutine());
    }

    bool IsLocalPlayer()
    {
        Mirror.NetworkIdentity identity = player.GetComponent<Mirror.NetworkIdentity>();

        if (!identity || !NetGuard.SessionActive)
            return true;

        return identity.isLocalPlayer;
    }

    IEnumerator SettleOnGroundRoutine()
    {
        yield return new WaitForSeconds(settleDelay);

        bool wasPaused = character != null;

        if (wasPaused)
            character.PausePlayer(PauseModes.FreezeInPlace);

        spawnPosition = ResolveSpawnPosition(spawnPosition);
        TeleportPlayer(spawnPosition, spawnRotation);

        if (wasPaused)
            character.UnpausePlayer();
    }

    public Vector3 ResolveSpawnPosition(Vector3 requested)
    {
        if (!snapToGround)
            return requested;

        bool useRequested = requested != Vector3.zero || hasRequestedPoint;
        return ResolveGround(useRequested ? requested : spawnPosition);
    }

    Vector3 ResolveGround(Vector3 position)
    {
        if (chunkGenerator)
            return new Vector3(position.x, chunkGenerator.GetHeightAt(position.x, position.z) + groundOffset, position.z);

        Vector3 origin = position + Vector3.up * probeHeight;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, probeDistance, groundMask, QueryTriggerInteraction.Ignore);

        float bestY = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform.IsChildOf(player) || hits[i].transform == player)
                continue;

            if (hits[i].point.y > bestY)
            {
                bestY = hits[i].point.y;
                found = true;
            }
        }

        if (found)
            return new Vector3(position.x, bestY + groundOffset, position.z);

        return position;
    }

    void TeleportPlayer(Vector3 position, Quaternion rotation)
    {
        if (playerBody)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.position = position;
            playerBody.rotation = rotation;
        }
        else
        {
            player.SetPositionAndRotation(position, rotation);
        }
    }

    public void SetSpawnPoint(Transform point)
    {
        if (!point) { return; }

        spawnPoint = point;
        spawnPosition = ResolveSpawnPosition(point.position);
        spawnRotation = point.rotation;
        hasRequestedPoint = true;
    }

    public void RespawnNow()
    {
        if (!respawning && !healthController)
            StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        respawning = true;

        if (character)
            character.PausePlayer(PauseModes.FreezeInPlace);

        yield return new WaitForSeconds(respawnDelay);

        Vector3 target = ResolveSpawnPosition(spawnPosition);
        TeleportPlayer(target, spawnRotation);

        if (restoreHealthOnRespawn && character && character.enableSurvivalStats)
        {
            float missing = character.defaultSurvivalStats.Health - character.currentSurvivalStats.Health;
            if (missing > 0f)
                character.ImmediateStateChange(missing, StatSelector.Health);
        }

        if (resetAmmoOnRespawn)
        {
            if (!gun)
                gun = player.GetComponentInChildren<SimpleShoot>();
            if (gun)
                gun.ResetAmmo();
        }

        if (character)
            character.UnpausePlayer();

        respawning = false;
    }
}