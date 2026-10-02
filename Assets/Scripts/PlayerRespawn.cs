using System.Collections;
using Mirror;
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
    private Vector3 requestedPoint;
    private bool hasRequestedPoint;
    private bool respawning;
    private bool initialized;
    private WorldChunkGenerator chunkGenerator;
    private PlayerHealth healthController;
    private NetworkIdentity networkIdentity;

    public Vector3 CurrentSpawnPosition => spawnPosition;
    public PlayerHealth HealthController => healthController;
    public bool IsNetworked => NetGuard.Networked(networkIdentity);

    void Awake()
    {
        networkIdentity = GetComponent<NetworkIdentity>();
        healthController = GetComponent<PlayerHealth>();
        chunkGenerator = FindFirstObjectByType<WorldChunkGenerator>();
    }

    void Start()
    {
        if (!Initialize())
            return;

        if (snapToGround && IsLocalPlayer())
            StartCoroutine(SettleOnGroundRoutine());
    }

    void Update()
    {
        if (!initialized)
        {
            if (!Initialize())
                return;
        }
        else if (!player)
        {
            initialized = false;
            return;
        }

        if (respawning || healthController || !HasAuthority())
            return;

        bool shouldRespawn = player.position.y < fallY;

        if (!shouldRespawn && character && character.enableSurvivalStats && character.currentSurvivalStats.Health <= 0f)
            shouldRespawn = true;

        if (shouldRespawn)
            StartCoroutine(RespawnRoutine());
    }

    bool Initialize()
    {
        if (player && IsWrongPlayer(player))
            player = null;

        if (!player)
            player = ResolveOwnPlayer();

        if (!player)
            return false;

        character = player.GetComponent<SUPERCharacterAIO>();
        playerBody = player.GetComponent<Rigidbody>();
        gun = player.GetComponentInChildren<SimpleShoot>();

        if (!chunkGenerator)
            chunkGenerator = FindFirstObjectByType<WorldChunkGenerator>();

        if (!hasRequestedPoint)
        {
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
        }

        initialized = true;
        return true;
    }

    Transform ResolveOwnPlayer()
    {
        if (character)
            return character.transform;

        SUPERCharacterAIO own = GetComponent<SUPERCharacterAIO>();

        if (own)
            return own.transform;

        if (spawnPoint && spawnPoint.GetComponent<SUPERCharacterAIO>())
            return spawnPoint;

        return null;
    }

    bool IsWrongPlayer(Transform candidate)
    {
        if (!NetGuard.SessionActive || !networkIdentity)
            return false;

        NetworkIdentity identity = candidate.GetComponent<NetworkIdentity>();

        if (!identity)
            return true;

        return identity != networkIdentity && !identity.isLocalPlayer;
    }

    bool HasAuthority()
    {
        if (!NetGuard.SessionActive)
            return true;

        return NetworkServer.active;
    }

    bool IsLocalPlayer()
    {
        if (!NetGuard.SessionActive || !networkIdentity)
            return true;

        return networkIdentity.isLocalPlayer;
    }

    IEnumerator SettleOnGroundRoutine()
    {
        yield return new WaitForSeconds(settleDelay);

        if (!player)
            yield break;

        bool wasPaused = PauseCharacter();

        spawnPosition = ResolveSpawnPosition(spawnPosition);
        TeleportPlayer(spawnPosition, spawnRotation);

        if (wasPaused)
            character.UnpausePlayer();
    }

    public Vector3 ResolveSpawnPosition(Vector3 requested)
    {
        if (!snapToGround)
            return requested;

        bool useRequested = hasRequestedPoint || requested != Vector3.zero;
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
            Transform hitTransform = hits[i].transform;

            if (player && (hitTransform == player || hitTransform.IsChildOf(player)))
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

    bool PauseCharacter()
    {
        if (!character || character.controllerPaused)
            return false;

        character.PausePlayer(PauseModes.FreezeInPlace);
        return true;
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
        else if (player)
        {
            player.SetPositionAndRotation(position, rotation);
        }
    }

    public void SetSpawnPoint(Transform point)
    {
        if (!point) { return; }

        spawnPoint = point;
        requestedPoint = point.position;
        hasRequestedPoint = true;
        spawnPosition = ResolveSpawnPosition(requestedPoint);
        spawnRotation = point.rotation;
    }

    public Vector3 RequestSpawnPosition(Vector3 point)
    {
        requestedPoint = point;
        hasRequestedPoint = true;
        spawnPosition = ResolveSpawnPosition(point);
        return spawnPosition;
    }

    public void ClearRequestedSpawn()
    {
        hasRequestedPoint = false;
        requestedPoint = Vector3.zero;

        if (spawnPoint)
        {
            spawnPosition = spawnPoint.position;
            spawnRotation = spawnPoint.rotation;
            return;
        }

        if (player)
        {
            spawnPosition = player.position;
            spawnRotation = player.rotation;
        }
    }

    public void RespawnNow()
    {
        if (respawning)
            return;

        if (healthController)
        {
            healthController.Kill();
            return;
        }

        if (!HasAuthority())
            return;

        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        respawning = true;

        bool wasPaused = PauseCharacter();

        yield return new WaitForSeconds(respawnDelay);

        Vector3 target = ResolveSpawnPosition(spawnPosition);
        TeleportPlayer(target, spawnRotation);

        if (restoreHealthOnRespawn && character && character.enableSurvivalStats)
        {
            float missing = character.defaultSurvivalStats.Health - character.currentSurvivalStats.Health;
            if (missing > 0f)
                character.ImmediateStateChange(missing, StatSelector.Health);
        }

        if (resetAmmoOnRespawn && gun)
            gun.ResetAmmo();

        if (wasPaused)
            character.UnpausePlayer();

        respawning = false;
    }
}