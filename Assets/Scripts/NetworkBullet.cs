using UnityEngine;
using Mirror;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkIdentity))]
public class NetworkBullet : NetworkBehaviour
{
    [Header("Bullet Settings")]
    [SerializeField] private float damage = 50f;
    [SerializeField] private float lifetime = 3f;

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnStartServer()
    {
        // На сервере включаем автоматическое уничтожение пули через время (если она никуда не попала)
        Invoke(nameof(DestroySelf), lifetime);
    }

void OnCollisionEnter(Collision collision)
    {
        if (!isServer)
            return;

        // Ищем PlayerHealth на объекте или его родителях
        PlayerHealth targetHealth = collision.gameObject.GetComponentInParent<PlayerHealth>();
        if (targetHealth != null)
        {
            // Вызываем метод урона напрямую на сервере, минуя клиентские проверки authority
            targetHealth.ApplyDamageFromServer(damage);
        }

        DestroySelf();
    }

    void DestroySelf()
    {
        // Проверяем, существует ли еще объект и активен ли сервер, чтобы избежать ошибок
        if (isServer && gameObject != null)
        {
            NetworkServer.Destroy(gameObject);
        }
    }
}