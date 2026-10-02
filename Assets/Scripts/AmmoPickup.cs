using UnityEngine;
using SUPERCharacter;

[RequireComponent(typeof(Collider))]
public class AmmoPickup : MonoBehaviour, ICollectable
{
    [SerializeField] private int ammoAmount = 7;
    [SerializeField] private bool destroyOnCollect = true;
    [SerializeField] private float spinSpeed = 45f;
    [SerializeField] private float bobHeight = 0.08f;
    [SerializeField] private bool animateRemotely = true;

    private Vector3 startPos;
    private bool collected;
    private bool syncedToServer;

    void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col)
            col.isTrigger = true;
    }

    void Start()
    {
        startPos = transform.position;

        Collider col = GetComponent<Collider>();
        if (col)
            col.isTrigger = true;
    }

    void Update()
    {
        if (collected || !animateRemotely || !IsLocalRelevant())
            return;

        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        transform.position = startPos + Vector3.up * (Mathf.Sin(Time.time * Mathf.PI) * bobHeight);
    }

    bool IsLocalRelevant()
    {
        if (!NetGuard.SessionActive)
            return true;

        Mirror.NetworkIdentity local = Mirror.NetworkClient.localPlayer;

        return local && Vector3.SqrMagnitude(local.transform.position - transform.position) < 60f * 60f;
    }

    public void Collect()
    {
        if (collected)
            return;

        NetworkShoot shoot = NetworkShoot.FindLocal();

        if (shoot && shoot.Gun)
        {
            if (syncedToServer)
                return;

            syncedToServer = true;
            shoot.RequestAmmo(ammoAmount);
            Despawn();
            return;
        }

        SimpleShoot gun = FindFirstObjectByType<SimpleShoot>();
        if (!gun)
            return;

        collected = true;
        gun.AddAmmo(ammoAmount);
        Despawn();
    }

    void Despawn()
    {
        collected = true;

        Mirror.NetworkIdentity identity = GetComponent<Mirror.NetworkIdentity>();

        if (NetGuard.Networked(identity))
        {
            if (Mirror.NetworkServer.active)
                Mirror.NetworkServer.Destroy(gameObject);

            return;
        }

        if (destroyOnCollect)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (collected || !BelongsToLocalPlayer(other))
            return;

        Collect();
    }

    bool BelongsToLocalPlayer(Collider other)
    {
        if (!NetGuard.SessionActive)
            return true;

        Mirror.NetworkIdentity identity = other.GetComponentInParent<Mirror.NetworkIdentity>();
        return identity && identity.isLocalPlayer;
    }
}
