using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
public class NetworkShoot : NetworkBehaviour
{
    [SerializeField] private SimpleShoot gun;
    [SerializeField] private bool lockRemotePlayers = true;

    public SimpleShoot Gun => gun;
    public bool Networked => NetGuard.Networked(this);

    void Awake()
    {
        if (!gun)
            gun = GetComponentInChildren<SimpleShoot>();
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