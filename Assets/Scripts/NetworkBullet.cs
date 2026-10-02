using System.Collections;
using Mirror;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkIdentity))]
public class NetworkBullet : NetworkBehaviour
{
    [Header("Bullet Settings")]
    [SerializeField] private float damage = 50f;
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private bool destroyOnAnyHit = true;

    private Rigidbody rb;
    private Collider bulletCollider;
    private PlayerHealth shooter;
    private bool despawned;

    public float Damage
    {
        get => damage;
        set => damage = value;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        bulletCollider = GetComponent<Collider>();
    }

    void Start()
    {
        PreparePhysics();
        StartCoroutine(LifetimeRoutine());
    }

    public override void OnStartClient()
    {
        // На клиенте физику ведёт сервер: без этого пуль отскакивал бы от локальных
        // копий игроков и сдвигал их без причины.
        if (!isServer)
            MakeClientSideVisual();
    }

    void PreparePhysics()
    {
        if (!rb || rb.isKinematic)
            return;

        // Коллайдер пули всего 2.5 мм, при скорости ~700 discrete-детекция
        // пропускает пулю сквозь капсулу игрока.
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public void IgnoreShooter(GameObject shooterObject, PlayerHealth shooterHealth)
    {
        shooter = shooterHealth;

        if (!shooterObject || !bulletCollider)
            return;

        Collider[] shooterColliders = shooterObject.GetComponentsInChildren<Collider>();

        for (int i = 0; i < shooterColliders.Length; i++)
            Physics.IgnoreCollision(bulletCollider, shooterColliders[i], true);
    }

    public void Launch(Vector3 velocity)
    {
        PreparePhysics();

        if (!rb)
            return;

        rb.linearVelocity = velocity;
    }

    IEnumerator LifetimeRoutine()
    {
        yield return new WaitForSeconds(lifetime);
        Despawn();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (despawned || !HasDamageAuthority())
            return;

        PlayerHealth target = collision.gameObject.GetComponentInParent<PlayerHealth>();

        if (target && target != shooter)
            target.ApplyDamageFromServer(damage);

        if (destroyOnAnyHit)
            Despawn();
    }

    bool HasDamageAuthority()
    {
        if (!NetGuard.SessionActive)
            return true;

        return isServer;
    }

    void MakeClientSideVisual()
    {
        if (rb)
            rb.isKinematic = true;

        if (bulletCollider)
            bulletCollider.enabled = false;
    }

    void Despawn()
    {
        if (despawned)
            return;

        if (!NetGuard.SessionActive || (netIdentity && netIdentity.isServer))
            despawned = true;
        else
            return;

        if (netIdentity && netIdentity.netId != 0)
            Mirror.NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }
}
