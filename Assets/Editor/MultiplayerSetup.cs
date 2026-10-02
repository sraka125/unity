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
        NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();

        if (!manager)
            return;

        RegisterSpawnPrefab(manager, FindPrefab("AmmoPickupTable"));
        RegisterSpawnPrefab(manager, FindPrefab("AmmoPickup"));

        EditorUtility.SetDirty(manager);
        Debug.Log("MultiplayerSetup: ammo pickup prefabs registered as network spawn prefabs.");
    }

    [MenuItem(MenuRoot + "Create Terrain Chunk Prefab", false, 23)]
    public static void CreateTerrainChunkPrefab()
    {
        TerrainData data = new TerrainData
        {
            heightmapResolution = 129,
            baseMapResolution = 256,
            size = new Vector3(50f, 24f, 50f)
        };

        GameObject terrainGo = Terrain.CreateTerrainGameObject(data);
        terrainGo.name = "TerrainChunk";
        terrainGo.transform.position = Vector3.zero;

        NetworkIdentity identity = terrainGo.AddComponent<NetworkIdentity>();
        TerrainChunkSync sync = terrainGo.AddComponent<TerrainChunkSync>();
        identity.sceneId = 0;

        const string folder = "Assets/Prefabs";
        string path = folder + "/TerrainChunk.prefab";

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(terrainGo, path);

        Object.DestroyImmediate(terrainGo);
        Object.DestroyImmediate(data);

        if (!prefab)
        {
            Debug.LogError("MultiplayerSetup: failed to save terrain chunk prefab.");
            return;
        }

        NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();

        if (manager)
        {
            RegisterSpawnPrefab(manager, prefab);
            EditorUtility.SetDirty(manager);
        }

        WorldChunkGenerator generator = Object.FindFirstObjectByType<WorldChunkGenerator>();

        if (generator)
        {
            SerializedObject serialized = new SerializedObject(generator);
            SerializedProperty property = serialized.FindProperty("terrainChunkPrefab");

            if (property != null)
            {
                property.objectReferenceValue = prefab;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(generator);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("MultiplayerSetup: terrain chunk prefab created at " + path);
    }

    [MenuItem(MenuRoot + "Remove NetworkIdentity From Scene Objects", false, 40)]
    public static void StripSceneIdentities()
    {
        NetworkIdentity[] identities = Object.FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None);
        int removed = 0;

        for (int i = 0; i < identities.Length; i++)
        {
            if (PrefabUtility.IsPartOfPrefabAsset(identities[i].gameObject))
                continue;

            if (identities[i].sceneId != 0)
                continue;

            Object.DestroyImmediate(identities[i]);
            removed++;
        }

        Debug.Log("MultiplayerSetup: removed " + removed + " NetworkIdentity component(s) without sceneId.");
    }

    static GameObject FindPrefab(string name)
    {
        string[] guids = AssetDatabase.FindAssets(name + " t:Prefab");

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab && prefab.name == name)
                return prefab;
        }

        return null;
    }

    static void RegisterSpawnPrefab(NetworkManager manager, GameObject prefab)
    {
        if (!manager || !prefab)
            return;

        if (prefab.GetComponent<NetworkIdentity>() == null)
            prefab.AddComponent<NetworkIdentity>();

        if (!manager.spawnPrefabs.Contains(prefab))
            manager.spawnPrefabs.Add(prefab);

        EditorUtility.SetDirty(prefab);
        AssetDatabase.SaveAssets();
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