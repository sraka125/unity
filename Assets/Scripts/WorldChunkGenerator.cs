using System.Collections.Generic;
using UnityEngine;
using SUPERCharacter;

public class WorldChunkGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private GameObject tablePrefab;
    [SerializeField] private GameObject ammoPrefab;
    [SerializeField] private GameObject ammoOnTablePrefab;
    [SerializeField] private GameObject terrainChunkPrefab;
    [SerializeField] private TerrainLayer terrainLayer;
    [SerializeField] private GameObject[] disableOnStart;
    [SerializeField] private bool autoSnapStartPositions = true;

    public event System.Action ChunksRebuilt;

    public Transform LocalPlayer => player;

    [Header("Chunk Grid")]
    [SerializeField] private float chunkSize = 50f;
    [SerializeField] private int viewRadius = 2;
    [SerializeField] private float groundY = -23.44f;
    [SerializeField] private int seed = 1337;

    [Header("Terrain Shape")]
    [SerializeField] private int heightmapResolution = 129;
    [SerializeField] private float terrainVerticalSize = 24f;
    [SerializeField] private float noiseScale = 0.02f;
    [SerializeField, Range(0f, 2f)] private float hillStrength = 1f;
    [SerializeField, Range(1, 8)] private int noiseOctaves = 4;
    [SerializeField, Range(1f, 3f)] private float lacunarity = 2f;
    [SerializeField, Range(0f, 1f)] private float gain = 0.5f;
    [SerializeField, Range(0f, 1f)] private float ridgeAmount = 0.4f;
    [SerializeField, Range(1f, 6f)] private float smoothingExponent = 1.6f;

    [Header("Terrain Look")]
    [SerializeField] private bool generateFallbackTexture = true;
    [SerializeField] private Color terrainColorLow = new Color(0.24f, 0.32f, 0.18f);
    [SerializeField] private Color terrainColorHigh = new Color(0.45f, 0.47f, 0.33f);
    [SerializeField] private float textureTileSize = 8f;

    [Header("Props Per Chunk")]
    [SerializeField] private int minTables = 1;
    [SerializeField] private int maxTables = 3;
    [SerializeField] private float edgePadding = 4f;
    [SerializeField] private bool placeAmmoOnTables = true;
    [SerializeField] private float looseAmmoChance = 0.35f;

    [Header("Ground Placement")]
    [SerializeField] private int placementAttempts = 32;
    [SerializeField, Range(0f, 90f)] private float maxGroundSlope = 32f;
    [SerializeField] private float slopeSampleRadius = 1f;
    [SerializeField] private float minTableSpacing = 7f;
    [SerializeField] private float propSink = 0.03f;
    [SerializeField, Range(0f, 1f)] private float alignToNormal = 0.6f;
    [SerializeField] private bool sleepPropsOnSpawn = true;

    [Header("Ammo Placement")]
    [SerializeField] private float ammoSurfaceOffset = 0.12f;
    [SerializeField] private float ammoSpread = 0.4f;

    readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    readonly List<Vector2Int> toRemove = new List<Vector2Int>();
    readonly List<Vector2Int> activeCoords = new List<Vector2Int>();

    TerrainLayer runtimeLayer;
    Texture2D runtimeTexture;

    Vector2Int lastCenter;
    bool hasCenter;
    bool initialized;
    bool setupDone;

    class Chunk
    {
        public GameObject Root;
        public Terrain Terrain;
        public System.Random Rng;
        public Vector3 Origin;
        public int TableCount;
        public bool LooseAmmo;
        public bool OwnsTerrainData;
        public readonly List<Vector3> Tables = new List<Vector3>();
        public readonly List<GameObject> NetProps = new List<GameObject>();
    }

    void Start()
    {
        TryInitialize();
    }

    void Update()
    {
        if (!initialized)
        {
            TryInitialize();
            return;
        }

        if (!player || !player.gameObject.activeInHierarchy)
            player = ResolveLocalPlayer();

        if (TerrainStreamingByServer())
        {
            if (!Mirror.NetworkServer.active)
                return;

            StreamChunksFromServer();
            return;
        }

        if (!player)
            return;

        Vector2Int center = WorldToChunk(player.position);
        if (hasCenter && center == lastCenter)
            return;

        lastCenter = center;
        hasCenter = true;
        RebuildChunks(center);
    }

    bool TryInitialize()
    {
        bool serverStreaming = TerrainStreamingByServer() && Mirror.NetworkServer.active;

        player = ResolveLocalPlayer();

        if (!player && !serverStreaming)
            return false;

        if (!setupDone)
        {
            setupDone = true;

            if (disableOnStart != null)
            {
                for (int i = 0; i < disableOnStart.Length; i++)
                {
                    if (disableOnStart[i])
                        disableOnStart[i].SetActive(false);
                }
            }

            if (!terrainLayer && generateFallbackTexture)
                terrainLayer = CreateFallbackLayer();

            if (autoSnapStartPositions && !TryGetComponent(out TerrainStartPositions _))
                gameObject.AddComponent<TerrainStartPositions>();
        }

        if (!TerrainStreamingByServer())
        {
            lastCenter = WorldToChunk(player.position);
            hasCenter = true;
            RebuildChunks(lastCenter);
        }
        else if (Mirror.NetworkServer.active)
        {
            StreamChunksFromServer();
        }

        initialized = true;
        return true;
    }

    Transform ResolveLocalPlayer()
    {
        if (NetGuard.SessionActive)
        {
            Mirror.NetworkIdentity local = Mirror.NetworkClient.localPlayer;

            if (local)
                return local.transform;
        }

        if (player)
            return player;

        SUPERCharacterAIO character = FindFirstObjectByType<SUPERCharacterAIO>();
        return character ? character.transform : null;
    }

    void OnDestroy()
    {
        if (runtimeTexture)
            Destroy(runtimeTexture);
    }

    void RebuildChunks(Vector2Int center)
    {
        activeCoords.Clear();

        for (int z = -viewRadius; z <= viewRadius; z++)
        {
            for (int x = -viewRadius; x <= viewRadius; x++)
            {
                Vector2Int coord = new Vector2Int(center.x + x, center.y + z);
                activeCoords.Add(coord);

                if (!chunks.ContainsKey(coord))
                    chunks[coord] = CreateChunk(coord);
            }
        }

        LinkTerrainNeighbors(activeCoords);

        toRemove.Clear();
        foreach (KeyValuePair<Vector2Int, Chunk> pair in chunks)
        {
            if (!activeCoords.Contains(pair.Key))
                toRemove.Add(pair.Key);
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            Vector2Int key = toRemove[i];
            if (chunks.TryGetValue(key, out Chunk chunk))
            {
                DestroyChunk(chunk);
                chunks.Remove(key);
            }
        }

        ChunksRebuilt?.Invoke();
    }

    bool TerrainStreamingByServer()
    {
        return NetGuard.SessionActive && terrainChunkPrefab && terrainChunkPrefab.GetComponent<Mirror.NetworkIdentity>();
    }

    void StreamChunksFromServer()
    {
        if (!TryResolveServerCenter(out Vector2Int center))
            return;

        if (hasCenter && center == lastCenter)
            return;

        lastCenter = center;
        hasCenter = true;
        RebuildChunks(center);
    }

    bool TryResolveServerCenter(out Vector2Int center)
    {
        center = Vector2Int.zero;

        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (KeyValuePair<int, Mirror.NetworkConnectionToClient> pair in Mirror.NetworkServer.connections)
        {
            Mirror.NetworkIdentity identity = pair.Value.identity;

            if (!identity)
                continue;

            sum += identity.transform.position;
            count++;
        }

        if (count == 0)
        {
            if (!player)
                return false;

            sum = player.position;
            count = 1;
        }

        center = WorldToChunk(sum / count);
        return true;
    }

    void LinkTerrainNeighbors(List<Vector2Int> coords)
    {
        for (int i = 0; i < coords.Count; i++)
        {
            Vector2Int coord = coords[i];
            if (!chunks.TryGetValue(coord, out Chunk chunk) || !chunk.Terrain)
                continue;

            chunk.Terrain.SetNeighbors(
                GetTerrainAt(coord + new Vector2Int(-1, 0)),
                GetTerrainAt(coord + new Vector2Int(0, 1)),
                GetTerrainAt(coord + new Vector2Int(1, 0)),
                GetTerrainAt(coord + new Vector2Int(0, -1)));
        }
    }

    Terrain GetTerrainAt(Vector2Int coord)
    {
        if (!chunks.TryGetValue(coord, out Chunk chunk) || !chunk.Terrain)
            return null;

        return chunk.Terrain;
    }

    Vector2Int WorldToChunk(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / chunkSize),
            Mathf.FloorToInt(worldPos.z / chunkSize));
    }

    Vector3 ChunkOrigin(Vector2Int coord)
    {
        return new Vector3(coord.x * chunkSize, groundY, coord.y * chunkSize);
    }

    public float GetHeightAt(float worldX, float worldZ)
    {
        Terrain terrain = GetTerrainAt(WorldToChunk(new Vector3(worldX, 0f, worldZ)));
        float sampled = SampleTerrainHeight(terrain, worldX, worldZ);

        if (sampled > float.NegativeInfinity)
            return sampled;

        return groundY + SampleHeightNormalized(worldX, worldZ) * terrainVerticalSize;
    }

    float SampleTerrainHeight(Terrain terrain, float worldX, float worldZ)
    {
        if (!terrain || !terrain.terrainData)
            return float.NegativeInfinity;

        Vector3 terrainPos = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;

        float u = (worldX - terrainPos.x) / size.x;
        float v = (worldZ - terrainPos.z) / size.z;

        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return float.NegativeInfinity;

        return terrainPos.y + terrain.terrainData.GetInterpolatedHeight(u, v);
    }

    Vector3 GetTerrainNormal(Terrain terrain, float worldX, float worldZ, float radius)
    {
        float left = GetHeightAt(worldX - radius, worldZ);
        float right = GetHeightAt(worldX + radius, worldZ);
        float back = GetHeightAt(worldX, worldZ - radius);
        float front = GetHeightAt(worldX, worldZ + radius);

        return new Vector3(left - right, radius * 2f, back - front).normalized;
    }

    public Vector3 GetTerrainNormalAt(float worldX, float worldZ, float radius)
    {
        return GetTerrainNormal(GetTerrainAt(WorldToChunk(new Vector3(worldX, 0f, worldZ))), worldX, worldZ, radius);
    }

    Chunk CreateChunk(Vector2Int coord)
    {
        Vector3 origin = ChunkOrigin(coord);

        bool networked = TerrainStreamingByServer() && Mirror.NetworkServer.active;

        GameObject root = null;

        if (!networked)
        {
            root = new GameObject($"Chunk_{coord.x}_{coord.y}");
            root.transform.SetParent(transform, false);
        }

        Chunk chunk = new Chunk
        {
            Root = root,
            Origin = origin
        };

        chunk.Terrain = CreateTerrain(coord, chunk, root ? root.transform : null);

        System.Random rng = new System.Random(HashCoord(coord));
        int tableCount = rng.Next(minTables, Mathf.Max(minTables, maxTables) + 1);
        bool looseAmmo = rng.NextDouble() < looseAmmoChance;

        chunk.Rng = rng;
        chunk.TableCount = tableCount;
        chunk.LooseAmmo = looseAmmo;

        if (!chunk.Terrain)
            return chunk;

        for (int i = 0; i < tableCount; i++)
            PlaceTableWithAmmo(chunk, rng);

        if (looseAmmo)
            PlaceLooseAmmo(chunk, rng);

        return chunk;
    }

    int GetHeightmapResolution()
    {
        int resolution = Mathf.Clamp(heightmapResolution, 33, 4097);
        return Mathf.ClosestPowerOfTwo(resolution - 1) + 1;
    }

    public void FillTerrain(Terrain terrain, Vector2Int coord)
    {
        if (!terrain)
            return;

        Vector3 origin = ChunkOrigin(coord);

        if (!terrain.terrainData)
            terrain.terrainData = new TerrainData();

        TerrainData data = terrain.terrainData;
        int resolution = GetHeightmapResolution();

        data.heightmapResolution = resolution;
        data.baseMapResolution = Mathf.Clamp(resolution / 4, 32, 512);
        data.size = new Vector3(chunkSize, terrainVerticalSize, chunkSize);

        if (terrainLayer)
            data.terrainLayers = new[] { terrainLayer };

        float[,] heights = new float[resolution, resolution];

        for (int z = 0; z < resolution; z++)
        {
            float worldZ = origin.z + z / (float)(resolution - 1) * chunkSize;

            for (int x = 0; x < resolution; x++)
            {
                float worldX = origin.x + x / (float)(resolution - 1) * chunkSize;
                heights[z, x] = SampleHeightNormalized(worldX, worldZ);
            }
        }

        data.SetHeights(0, 0, heights);

        terrain.transform.position = origin;
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 5f;
        terrain.basemapDistance = 150f;
    }

    Terrain CreateLocalTerrain(Vector2Int coord, Transform parent)
    {
        GameObject terrainGo = Terrain.CreateTerrainGameObject(new TerrainData());
        terrainGo.name = $"Terrain_{coord.x}_{coord.y}";
        terrainGo.transform.SetParent(parent, false);

        Terrain terrain = terrainGo.GetComponent<Terrain>();
        FillTerrain(terrain, coord);

        return terrain;
    }

    Terrain CreateNetworkedTerrain(Vector2Int coord, Chunk chunk)
    {
        GameObject terrainGo = Instantiate(terrainChunkPrefab);
        terrainGo.name = $"Terrain_{coord.x}_{coord.y}";

        Terrain terrain = terrainGo.GetComponent<Terrain>();

        if (!terrain)
            return null;

        Mirror.NetworkIdentity identity = terrainGo.GetComponent<Mirror.NetworkIdentity>();

        if (!identity)
            identity = terrainGo.AddComponent<Mirror.NetworkIdentity>();

        if (terrain.terrainData)
            terrain.terrainData = Instantiate(terrain.terrainData);

        TerrainChunkSync sync = terrainGo.GetComponent<TerrainChunkSync>();

        if (!sync)
            sync = terrainGo.AddComponent<TerrainChunkSync>();

        FillTerrain(terrain, coord);
        sync.SetCoord(coord);

        Mirror.NetworkServer.Spawn(terrainGo);

        chunk.NetProps.Add(terrainGo);
        chunk.OwnsTerrainData = true;

        return terrain;
    }

    Terrain CreateTerrain(Vector2Int coord, Chunk chunk, Transform parent)
    {
        bool networked = TerrainStreamingByServer();

        if (networked && Mirror.NetworkServer.active)
            return CreateNetworkedTerrain(coord, chunk);

        if (networked)
            return null;

        return CreateLocalTerrain(coord, parent);
    }

    float SampleHeightNormalized(float worldX, float worldZ)
    {
        if (noiseOctaves <= 0)
            return 0f;

        float offsetX = seed * 0.173f;
        float offsetZ = seed * 0.419f;

        float amplitude = 1f;
        float frequency = 1f;
        float sum = 0f;
        float norm = 0f;

        for (int i = 0; i < noiseOctaves; i++)
        {
            float sampleX = (worldX + offsetX) * noiseScale * frequency;
            float sampleZ = (worldZ + offsetZ) * noiseScale * frequency;
            float n = Mathf.PerlinNoise(sampleX, sampleZ);

            if (ridgeAmount > 0f)
            {
                float ridge = 1f - Mathf.Abs(n * 2f - 1f);
                ridge *= ridge;
                n = Mathf.Lerp(n, ridge, ridgeAmount);
            }

            sum += n * amplitude;
            norm += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        float blended = norm > 0f ? sum / norm : 0f;
        float shaped = Mathf.Pow(Mathf.Clamp01(blended), smoothingExponent);

        return Mathf.Clamp01(shaped * hillStrength);
    }

    TerrainLayer CreateFallbackLayer()
    {
        const int size = 256;
        runtimeTexture = new Texture2D(size, size, TextureFormat.RGB24, true)
        {
            name = "GeneratedTerrainTexture",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4
        };

        Color[] pixels = new Color[size * size];
        float offsetX = seed * 0.173f;
        float offsetZ = seed * 0.419f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size * 8f;
                float v = y / (float)size * 8f;
                float detail = Mathf.PerlinNoise(u + offsetX, v + offsetZ);
                float grain = Mathf.PerlinNoise(u * 3.7f + offsetZ, v * 3.7f + offsetX);
                float t = Mathf.Clamp01(detail * 0.7f + grain * 0.3f);
                pixels[y * size + x] = Color.Lerp(terrainColorLow, terrainColorHigh, t);
            }
        }

        runtimeTexture.SetPixels(pixels);
        runtimeTexture.Apply(true, false);

        runtimeLayer = new TerrainLayer
        {
            name = "GeneratedTerrainLayer",
            diffuseTexture = runtimeTexture,
            tileSize = new Vector2(textureTileSize, textureTileSize),
            tileOffset = Vector2.zero,
            specular = Color.black,
            metallic = 0f,
            smoothness = 0f
        };

        return runtimeLayer;
    }

    void PlaceTableWithAmmo(Chunk chunk, System.Random rng)
    {
        if (!tablePrefab) { return; }

        if (!TryFindGroundSpot(chunk.Origin, chunk, rng, minTableSpacing, out Vector3 pos, out Vector3 normal))
            return;

        Quaternion rot = BuildPlacementRotation(normal, (float)rng.NextDouble() * 360f);

        GameObject table = SpawnProp(tablePrefab, pos, rot, chunk, "Table", out _);

        if (!table)
            return;

        if (sleepPropsOnSpawn)
            SleepBody(table);

        chunk.Tables.Add(pos);

        if (!placeAmmoOnTables)
            return;

        GameObject ammoPrefabToUse = ammoOnTablePrefab ? ammoOnTablePrefab : ammoPrefab;

        if (!ammoPrefabToUse)
            return;

        float topY = GetHighestPoint(table, pos);
        Vector3 ammoPos = new Vector3(
            pos.x + ((float)rng.NextDouble() - 0.5f) * ammoSpread,
            topY + ammoSurfaceOffset,
            pos.z + ((float)rng.NextDouble() - 0.5f) * ammoSpread);

        SpawnProp(ammoPrefabToUse, ammoPos, Quaternion.identity, chunk, "AmmoOnTable", out _);
    }

    void PlaceLooseAmmo(Chunk chunk, System.Random rng)
    {
        if (!ammoPrefab) { return; }

        if (!TryFindGroundSpot(chunk.Origin, chunk, rng, 1.5f, out Vector3 pos, out _))
            return;

        Vector3 ammoPos = new Vector3(pos.x, GetHeightAt(pos.x, pos.z) + ammoSurfaceOffset, pos.z);
        SpawnProp(ammoPrefab, ammoPos, Quaternion.identity, chunk, "AmmoLoose", out _);
    }

    GameObject SpawnProp(GameObject prefab, Vector3 position, Quaternion rotation, Chunk chunk, string propName, out bool spawnedOverNetwork)
    {
        spawnedOverNetwork = false;

        if (!prefab)
            return null;

        Mirror.NetworkIdentity identity = prefab.GetComponent<Mirror.NetworkIdentity>();
        bool networked = identity != null && NetGuard.SessionActive;

        if (networked)
        {
            if (!Mirror.NetworkServer.active)
                return null;

            GameObject netInstance = Instantiate(prefab, position, rotation);
            netInstance.name = propName;

            Mirror.NetworkServer.Spawn(netInstance);

            chunk.NetProps.Add(netInstance);
            spawnedOverNetwork = true;

            return netInstance;
        }

        GameObject instance = Instantiate(prefab, position, rotation, chunk.Root.transform);
        instance.name = propName;
        return instance;
    }

    bool TryFindGroundSpot(Vector3 origin, Chunk chunk, System.Random rng, float spacing, out Vector3 spot, out Vector3 normal)
    {
        spot = Vector3.zero;
        normal = Vector3.up;

        float min = edgePadding;
        float max = Mathf.Max(edgePadding, chunkSize - edgePadding);
        Vector3 best = Vector3.zero;
        float bestSlope = float.PositiveInfinity;
        bool hasBest = false;

        for (int attempt = 0; attempt < placementAttempts; attempt++)
        {
            float localX = Mathf.Lerp(min, max, (float)rng.NextDouble());
            float localZ = Mathf.Lerp(min, max, (float)rng.NextDouble());
            float worldX = origin.x + localX;
            float worldZ = origin.z + localZ;

            if (spacing > 0f && !hasBest && IsCrowded(chunk, worldX, worldZ, spacing))
                continue;

            float height = GetHeightAt(worldX, worldZ);

            if (height == float.NegativeInfinity)
                continue;

            Vector3 candidateNormal = GetTerrainNormal(chunk.Terrain, worldX, worldZ, slopeSampleRadius);
            float slope = Vector3.Angle(candidateNormal, Vector3.up);
            Vector3 candidate = new Vector3(worldX, height, worldZ) - candidateNormal * propSink;

            if (!hasBest || slope < bestSlope)
            {
                best = candidate;
                bestSlope = slope;
                normal = candidateNormal;
                hasBest = true;
            }

            if (slope <= maxGroundSlope)
            {
                spot = candidate;
                normal = candidateNormal;
                return true;
            }
        }

        if (hasBest)
        {
            spot = best;
            return true;
        }

        return false;
    }

    bool IsCrowded(Chunk chunk, float worldX, float worldZ, float spacing)
    {
        for (int i = 0; i < chunk.Tables.Count; i++)
        {
            Vector3 other = chunk.Tables[i];
            float dx = other.x - worldX;
            float dz = other.z - worldZ;

            if (dx * dx + dz * dz < spacing * spacing)
                return true;
        }

        return false;
    }

    Quaternion BuildPlacementRotation(Vector3 normal, float yaw)
    {
        Quaternion align = Quaternion.FromToRotation(Vector3.up, normal);
        Quaternion spin = Quaternion.AngleAxis(yaw, normal);

        if (alignToNormal <= 0f)
            return Quaternion.Euler(0f, yaw, 0f);

        return Quaternion.Slerp(Quaternion.Euler(0f, yaw, 0f), spin * align, alignToNormal);
    }

    float GetHighestPoint(GameObject instance, Vector3 pivot)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        float highest = pivot.y;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].bounds.max.y > highest)
                highest = renderers[i].bounds.max.y;
        }

        return highest;
    }

    void SleepBody(GameObject instance)
    {
        Rigidbody body = instance.GetComponentInChildren<Rigidbody>();

        if (body && !body.isKinematic)
            body.Sleep();
    }

    void DestroyChunk(Chunk chunk)
    {
        if (NetGuard.SessionActive && Mirror.NetworkServer.active)
        {
            for (int i = 0; i < chunk.NetProps.Count; i++)
            {
                GameObject netProp = chunk.NetProps[i];

                if (netProp)
                    Mirror.NetworkServer.Destroy(netProp);
            }
        }

        chunk.NetProps.Clear();

        Terrain terrain = chunk.Terrain;

        if (terrain && terrain.terrainData)
        {
            if (chunk.OwnsTerrainData || chunk.Root)
                Destroy(terrain.terrainData);
            else
                terrain.terrainData = null;
        }

        if (chunk.Root)
            Destroy(chunk.Root);
    }

    int HashCoord(Vector2Int coord)
    {
        unchecked
        {
            int hash = seed;
            hash = (hash * 397) ^ coord.x;
            hash = (hash * 397) ^ coord.y;
            return hash;
        }
    }
}