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
        // Вся логика столкновений и урона в сетевой игре должна обрабатываться ТОЛЬКО на сервере
        if (!isServer)
            return;

        // Проверяем, есть ли у объекта, в который попали, компонент здоровья
        PlayerHealth targetHealth = collision.gameObject.GetComponent<PlayerHealth>();
        if (targetHealth != null)
        {
            // Наносим урон конкретному игроку
            targetHealth.ApplyDamage(damage);
        }

        // Уничтожаем пулю при столкновении
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