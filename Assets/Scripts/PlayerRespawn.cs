using System.Collections;
using UnityEngine;
using SUPERCharacter;

public class PlayerRespawn : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float fallY = -40f;
    [SerializeField] private float respawnDelay = 1f;
    [SerializeField] private bool resetAmmoOnRespawn = true;
    [SerializeField] private bool restoreHealthOnRespawn = true;

    private SUPERCharacterAIO character;
    private Rigidbody playerBody;
    private SimpleShoot gun;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private bool respawning;

    void Start()
    {
        if (!player)
        {
            character = FindFirstObjectByType<SUPERCharacterAIO>();
            if (character)
                player = character.transform;
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

    void Update()
    {
        if (respawning || !player) { return; }

        bool shouldRespawn = player.position.y < fallY;

        if (!shouldRespawn && character && character.enableSurvivalStats && character.currentSurvivalStats.Health <= 0f)
            shouldRespawn = true;

        if (shouldRespawn)
            StartCoroutine(RespawnRoutine());
    }

    public void SetSpawnPoint(Transform point)
    {
        if (!point) { return; }

        spawnPoint = point;
        spawnPosition = point.position;
        spawnRotation = point.rotation;
    }

    public void RespawnNow()
    {
        if (!respawning)
            StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        respawning = true;

        if (character)
            character.PausePlayer(PauseModes.FreezeInPlace);

        yield return new WaitForSeconds(respawnDelay);

        if (playerBody)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.position = spawnPosition;
            playerBody.rotation = spawnRotation;
        }
        else
        {
            player.SetPositionAndRotation(spawnPosition, spawnRotation);
        }

        if (restoreHealthOnRespawn && character && character.enableSurvivalStats)
        {
            float missing = character.defaultSurvivalStats.Health - character.currentSurvivalStats.Health;
            if (missing > 0f)
                character.ImmediateStateChange(missing, StatSelector.Health);
        }

        if (resetAmmoOnRespawn)
        {
            if (!gun)
                gun = FindFirstObjectByType<SimpleShoot>();
            if (gun)
                gun.ResetAmmo();
        }

        if (character)
            character.UnpausePlayer();

        respawning = false;
    }
}
