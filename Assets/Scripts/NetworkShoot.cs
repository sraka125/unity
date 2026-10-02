using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
public class NetworkShoot : NetworkBehaviour
{
    [SerializeField] private SimpleShoot gun;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private bool lockRemotePlayers = true;

    public SimpleShoot Gun => gun;
    public bool Networked => NetGuard.Networked(this);

    void Awake()
    {
        if (!gun)
            gun = GetComponentInChildren<SimpleShoot>();
    }

    public GameObject BulletPrefab
    {
        get
        {
            if (bulletPrefab)
                return bulletPrefab;

            return gun ? gun.bulletPrefab : null;
        }
    }

    public override void OnStartClient()
    {
        ApplyInputLock();
    }

    public override void OnStartServer()
    {
        ApplyInputLock();
    }

    public override void OnStartLocalPlayer()
    {
        ApplyInputLock();
    }

    public override void OnStopLocalPlayer()
    {
        ApplyInputLock();
    }

    public override void OnStopClient()
    {
        if (gun)
            gun.enabled = true;
    }

    void ApplyInputLock()
    {
        if (!gun || !lockRemotePlayers)
            return;

        bool local = isLocalPlayer;

        if (gun.enabled != local)
            gun.enabled = local;
    }

    public void RequestAmmo(int amount)
    {
        if (amount <= 0)
            return;

        if (Networked)
            CmdRequestAmmo(amount);
        else
            GrantAmmo(amount);
    }

    [Command]
    public void CmdRequestAmmo(int amount)
    {
        if (!isOwned)
            return;

        RpcGrantAmmo(amount);
    }

    [ClientRpc]
    public void RpcGrantAmmo(int amount)
    {
        if (isOwned)
            GrantAmmo(amount);
    }

    void GrantAmmo(int amount)
    {
        if (gun)
            gun.AddAmmo(amount);
    }

    public void RequestBulletSpawn(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        if (!Networked)
        {
            SpawnBulletLocally(position, rotation, velocity);
            return;
        }

        if (!isOwned)
            return;

        CmdSpawnBullet(position, rotation, velocity);
    }

    [Command]
    public void CmdSpawnBullet(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        if (!isOwned)
            return;

        SpawnBulletOnServer(position, rotation, velocity);
    }

    void SpawnBulletOnServer(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        GameObject prefab = BulletPrefab;

        if (!prefab || !Mirror.NetworkServer.active)
            return;

        GameObject instance = Instantiate(prefab, position, rotation);
        NetworkBullet bullet = instance.GetComponent<NetworkBullet>();

        if (bullet)
            bullet.IgnoreShooter(gameObject, GetComponent<PlayerHealth>());

        if (bullet)
            bullet.Launch(velocity);
        else
        {
            Rigidbody body = instance.GetComponent<Rigidbody>();

            if (body)
                body.linearVelocity = velocity;
        }

        Mirror.NetworkServer.Spawn(instance);
    }

    public void SpawnBulletLocally(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        GameObject prefab = BulletPrefab;

        if (!prefab)
            return;

        GameObject instance = Instantiate(prefab, position, rotation);
        NetworkBullet bullet = instance.GetComponent<NetworkBullet>();

        if (bullet)
            bullet.IgnoreShooter(gameObject, GetComponent<PlayerHealth>());

        if (bullet)
            bullet.Launch(velocity);
        else
        {
            Rigidbody body = instance.GetComponent<Rigidbody>();

            if (body)
                body.linearVelocity = velocity;
        }
    }

    public static NetworkShoot FindLocal()
    {
        if (!NetGuard.SessionActive)
            return null;

        NetworkIdentity local = NetworkClient.localPlayer;

        if (!local)
            return null;

        return local.GetComponent<NetworkShoot>();
    }
}