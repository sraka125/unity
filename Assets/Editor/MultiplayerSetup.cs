using kcp2k;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MultiplayerSetup
{
    private const string MenuRoot = "Tools/Mirror/";

    [MenuItem(MenuRoot + "Setup Network Scene And Player Prefab", false, 0)]
    public static void SetupAll()
    {
        GameObject playerPrefab = ResolvePlayerPrefab();

        if (!playerPrefab)
        {
            EditorUtility.DisplayDialog("Mirror Setup",
                "Player prefab not found. Assign it in WorldChunkGenerator or open the prefab first.", "OK");
            return;
        }

        ConfigurePlayerPrefab(playerPrefab);
        ConfigureNetworkManager(playerPrefab);
        ConfigureAmmoPickup(playerPrefab);

        EditorUtility.DisplayDialog("Mirror Setup", "Done. Player prefab: " + playerPrefab.name, "OK");
    }

    [MenuItem(MenuRoot + "Add NetworkManager To Scene", false, 20)]
    public static void ConfigureNetworkManager(GameObject playerPrefab)
    {
        if (playerPrefab)
            SetupPlayerPrefab(playerPrefab);

        NetworkManager existing = Object.FindFirstObjectByType<NetworkManager>();

        if (existing)
        {
            if (playerPrefab)
                existing.playerPrefab = playerPrefab;

            Debug.Log("MultiplayerSetup: existing NetworkManager updated.");
            EditorUtility.SetDirty(existing);
            return;
        }

        GameObject go = new GameObject("NetworkManager");
        NetworkManager manager = go.AddComponent<NetworkManager>();

        if (playerPrefab)
            manager.playerPrefab = playerPrefab;

        go.AddComponent<KcpTransport>();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("MultiplayerSetup: NetworkManager created.");
    }

    [MenuItem(MenuRoot + "Configure Player Prefab", false, 21)]
    public static void ConfigurePlayerPrefab(GameObject playerPrefab)
    {
        if (!playerPrefab)
        {
            Debug.LogWarning("MultiplayerSetup: no player prefab resolved.");
            return;
        }

        SetupPlayerPrefab(playerPrefab);
        EditorUtility.SetDirty(playerPrefab);
        PrefabUtility.SavePrefabAsset(playerPrefab);
        Debug.Log("MultiplayerSetup: player prefab configured -> " + playerPrefab.name);
    }

    [MenuItem(MenuRoot + "Add Network Spawn Components To Ammo Pickup", false, 22)]
    public static void ConfigureAmmoPickup(GameObject playerPrefab)
    {
        string[] guids = AssetDatabase.FindAssets("AmmoPickup t:Prefab");

        if (guids.Length == 0)
        {
            Debug.LogWarning("MultiplayerSetup: AmmoPickup prefab not found.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));

        if (prefab && prefab.GetComponent<NetworkIdentity>() == null)
            prefab.AddComponent<NetworkIdentity>();
    }

    static void SetupPlayerPrefab(GameObject playerPrefab)
    {
        if (playerPrefab.GetComponent<NetworkIdentity>() == null)
            playerPrefab.AddComponent<NetworkIdentity>();

        if (playerPrefab.GetComponent<NetworkTransformHybrid>() == null)
            playerPrefab.AddComponent<NetworkTransformHybrid>();

        if (playerPrefab.GetComponent<PlayerHealth>() == null)
            playerPrefab.AddComponent<PlayerHealth>();

        if (playerPrefab.GetComponent<PlayerRespawn>() == null)
            playerPrefab.AddComponent<PlayerRespawn>();

        SimpleShoot gun = playerPrefab.GetComponentInChildren<SimpleShoot>(true);

        if (gun && gun.GetComponent<NetworkShoot>() == null)
            gun.gameObject.AddComponent<NetworkShoot>();
    }

    static GameObject ResolvePlayerPrefab()
    {
        WorldChunkGenerator generator = Object.FindFirstObjectByType<WorldChunkGenerator>();

        if (generator)
        {
            SimpleShoot gun = generator.GetComponentInChildren<SimpleShoot>(true);

            if (gun)
            {
                GameObject prefab = PrefabUtility.GetOutermostPrefabInstanceRoot(gun.gameObject);

                if (prefab)
                    return PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            }
        }

        return null;
    }
}