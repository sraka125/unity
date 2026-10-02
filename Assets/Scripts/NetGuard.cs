using Mirror;
using SUPERCharacter;
using UnityEngine;

public static class NetGuard
{
    public static bool SessionActive =>
        NetworkManager.singleton != null && NetworkManager.singleton.isNetworkActive;

    public static bool Networked(NetworkBehaviour behaviour) =>
        behaviour != null && Networked(behaviour.netIdentity);

    public static bool Networked(NetworkIdentity identity) =>
        identity != null && SessionActive;

    public static bool HasAuthority(NetworkBehaviour behaviour) =>
        !Networked(behaviour) || behaviour.isServer;

    public static bool IsLocal(NetworkBehaviour behaviour) =>
        !Networked(behaviour) || behaviour.isLocalPlayer;
}