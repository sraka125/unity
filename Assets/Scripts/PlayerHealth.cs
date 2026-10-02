using System.Collections;
using System.Collections.Generic;
using Mirror;
using SUPERCharacter;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
public class PlayerHealth : NetworkBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 250f;
    [SerializeField] private bool useSurvivalStats = true;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 3f;
    [SerializeField] private float fallY = -40f;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private bool searchGroundOnRespawn = true;

    [SyncVar(hook = nameof(OnHealthChanged))] private float health;
    [SyncVar] private float syncedMaxHealth;
    [SyncVar(hook = nameof(OnDeadChanged))] private bool dead;

    public float Health => health;
    public float MaxHealth => syncedMaxHealth > 0f ? syncedMaxHealth : maxHealth;
    public bool IsDead => dead;
    public bool Networked => NetGuard.Networked(this);
    public bool CanBeDamaged => !dead && health > 0f;

    public event System.Action<float, float> HealthChanged;
    public event System.Action<bool> DeadChanged;

    private SUPERCharacterAIO character;
    private Rigidbody playerBody;
    private PlayerRespawn respawn;
    private WorldChunkGenerator chunkGenerator;
    private Coroutine respawnRoutine;
    private bool paused;
    [Header("Damage by Object Dictionary")]
    [SerializeField] private List<DamageMapping> damageMappings = new List<DamageMapping>();

    [System.Serializable]
    public struct DamageMapping
    {
        public string objectTag; // Или можно использовать другой ключ (например, имя)
        public float damageAmount;
    }

    private Dictionary<string, float> damageDictionary = new Dictionary<string, float>();

    void Awake()
    {
        character = GetComponent<SUPERCharacterAIO>();
        playerBody = GetComponent<Rigidbody>();
        respawn = GetComponent<PlayerRespawn>();
        chunkGenerator = FindFirstObjectByType<WorldChunkGenerator>();
        // Заполняем словарь из списка для удобной настройки в инспекторе
        foreach (var mapping in damageMappings)
        {
            if (!string.IsNullOrEmpty(mapping.objectTag) && !damageDictionary.ContainsKey(mapping.objectTag))
            {
                damageDictionary.Add(mapping.objectTag, mapping.damageAmount);
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // Урон должен обрабатывать только сервер в мультиплеере
        if (NetGuard.SessionActive && !Mirror.NetworkServer.active)
            return;

        CheckAndApplyDamage(collision.gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (NetGuard.SessionActive && !Mirror.NetworkServer.active)
            return;

        CheckAndApplyDamage(other.gameObject);
    }

    void CheckAndApplyDamage(GameObject hitObject)
    {
        // Проверяем по тегу объекта (ключ словаря)
        string objTag = hitObject.tag;

        if (damageDictionary.TryGetValue(objTag, out float damage))
        {
            ApplyDamage(damage);
        }
    }

    void Start()
    {
        if (!Networked)
        {
            syncedMaxHealth = ResolveMaxHealth();
            health = syncedMaxHealth;
            dead = false;
            ApplyHealthToCharacter(health);
        }
    }

    public override void OnStartServer()
    {
        if (respawnRoutine != null)
        {
            StopCoroutine(respawnRoutine);
            respawnRoutine = null;
        }

        syncedMaxHealth = ResolveMaxHealth();
        health = syncedMaxHealth;
        dead = false;

        ApplyDeadState(false);
        ApplyHealthToCharacter(health);
    }

    public override void OnStartClient()
    {
        ApplyHealthToCharacter(health);
        ApplyDeadState(dead);
    }

    void Update()
    {
        if (!NetGuard.HasAuthority(this) || !CanBeDamaged)
            return;

        if (fallY < 0f && transform.position.y < fallY)
            Kill();
    }

    public void ApplyDamage(float amount)
    {
        if (!NetGuard.HasAuthority(this))
        {
            CmdRequestDamage(amount);
            return;
        }

        DamageOnServer(amount);
    }

    // Вызывается напрямую сервером (например, из пули)
    public void ApplyDamageFromServer(float amount)
    {
        if (!Mirror.NetworkServer.active)
            return;

        DamageOnServer(amount);
    }

    [Command]
    public void CmdRequestDamage(float amount)
    {
        DamageOnServer(amount);
    }

    public void Heal(float amount)
    {
        if (!NetGuard.HasAuthority(this))
        {
            CmdRequestHeal(amount);
            return;
        }

        HealOnServer(amount);
    }

    [Command]
    public void CmdRequestHeal(float amount)
    {
        HealOnServer(amount);
    }

    public void Kill()
    {
        if (!NetGuard.HasAuthority(this))
        {
            CmdRequestKill();
            return;
        }

        KillOnServer();
    }

    [Command]
    public void CmdRequestKill()
    {
        KillOnServer();
    }

    void DamageOnServer(float amount)
    {
        if (!CanBeDamaged || amount <= 0f)
            return;

        SetHealth(health - amount);

        if (health <= 0f)
            KillOnServer();
    }

    void HealOnServer(float amount)
    {
        if (!CanBeDamaged || amount <= 0f)
            return;

        SetHealth(Mathf.Min(health + amount, MaxHealth));
    }

    void KillOnServer()
    {
        if (dead)
            return;

        dead = true;
        SetHealth(0f);
        ApplyDeadState(true);

        if (respawnRoutine != null)
            StopCoroutine(respawnRoutine);

        respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);

        Vector3 point = Object.FindAnyObjectByType<NetworkStartPosition>().transform.position;

        dead = false;
        SetHealth(MaxHealth);
        ApplyRespawn(point);

        if (Networked)
            RpcApplyRespawn(point);

        respawnRoutine = null;
    }

    [ClientRpc]
    public void RpcApplyRespawn(Vector3 point)
    {
        ApplyRespawn(point);
    }

    void ApplyRespawn(Vector3 point)
    {
        ApplyDeadState(false);
        ApplyHealthToCharacter(health);
        Teleport(point);
    }

    Vector3 GetRespawnPoint()
    {
        Vector3 point;

        if (spawnPoint)
            point = spawnPoint.position;
        else if (respawn)
            point = respawn.CurrentSpawnPosition;
        else
            point = transform.position + Vector3.up * 2f;

        if (searchGroundOnRespawn && respawn)
            point = respawn.ResolveSpawnPosition(point);
        else if (searchGroundOnRespawn && chunkGenerator)
            point = new Vector3(point.x, chunkGenerator.GetHeightAt(point.x, point.z) + 0.1f, point.z);

        return point;
    }

    void Teleport(Vector3 point)
    {
        Quaternion rotation = spawnPoint ? spawnPoint.rotation : transform.rotation;

        if (playerBody)
        {
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.position = point;
            playerBody.rotation = rotation;
        }
        else
        {
            transform.SetPositionAndRotation(point, rotation);
        }
    }

    float ResolveMaxHealth()
    {
        if (useSurvivalStats && character && character.defaultSurvivalStats.Health > 0f)
            return character.defaultSurvivalStats.Health;

        return maxHealth;
    }

    void SetHealth(float value)
    {
        health = Mathf.Clamp(value, 0f, MaxHealth);

        if (NetGuard.HasAuthority(this))
            ApplyHealthToCharacter(health);
    }

    void ApplyHealthToCharacter(float value)
    {
        if (!useSurvivalStats || !character || !character.enableSurvivalStats)
            return;

        float delta = value - character.currentSurvivalStats.Health;

        if (Mathf.Abs(delta) > 0.001f)
            character.ImmediateStateChange(delta, StatSelector.Health);
    }

    void ApplyDeadState(bool value)
    {
        if (!character || paused == value)
            return;

        paused = value;

        if (value)
            character.PausePlayer(PauseModes.FreezeInPlace);
        else
            character.UnpausePlayer();
    }

    void OnHealthChanged(float oldValue, float newValue)
    {
        if (!NetGuard.HasAuthority(this))
            ApplyHealthToCharacter(newValue);

        HealthChanged?.Invoke(newValue, MaxHealth);
    }

    void OnDeadChanged(bool oldValue, bool newValue)
    {
        ApplyDeadState(newValue);
        DeadChanged?.Invoke(newValue);
    }
}