using System.Collections.Generic;
using UnityEngine;
using SUPERCharacter;

public class WorldChunkGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private GameObject tablePrefab;
    [SerializeField] private GameObject ammoPrefab;
    [SerializeField] private TerrainLayer terrainLayer;
    [SerializeField] private GameObject[] disableOnStart;

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
    [SerializeField] private float tableSurfaceOffset = 0.95f;
    [SerializeField] private float edgePadding = 4f;
    [SerializeField] private bool placeAmmoOnTables = true;
    [SerializeField] private float looseAmmoChance = 0.35f;

    readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    readonly List<Vector2Int> toRemove = new List<Vector2Int>();
    readonly List<Vector2Int> activeCoords = new List<Vector2Int>();

    TerrainLayer runtimeLayer;
    Texture2D runtimeTexture;

    Vector2Int lastCenter;
    bool hasCenter;
    bool initialized;

    class Chunk
    {
        public GameObject Root;
        public Terrain Terrain;
        public System.Random Rng;
        public int TableCount;
        public bool LooseAmmo;
    }

    void Start()
    {
        if (!player)
        {
            SUPERCharacterAIO character = FindFirstObjectByType<SUPERCharacterAIO>();
            if (character)
                player = character.transform;
        }

        if (!player)
        {
            Debug.LogWarning("WorldChunkGenerator: player not found.");
            enabled = false;
            return;
        }

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

        lastCenter = WorldToChunk(player.position);
        hasCenter = true;
        initialized = true;

        RebuildChunks(lastCenter);
    }

    void Update()
    {
        if (!initialized || !player)
            return;

        Vector2Int center = WorldToChunk(player.position);
        if (hasCenter && center == lastCenter)
            return;

        lastCenter = center;
        hasCenter = true;
        RebuildChunks(center);
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
        return groundY + SampleHeightNormalized(worldX, worldZ) * terrainVerticalSize;
    }

    Chunk CreateChunk(Vector2Int coord)
    {
        Vector3 origin = ChunkOrigin(coord);

        GameObject root = new GameObject($"Chunk_{coord.x}_{coord.y}");
        root.transform.SetParent(transform, false);

        Terrain terrain = CreateTerrain(origin, root.transform);

        System.Random rng = new System.Random(HashCoord(coord));
        int tableCount = rng.Next(minTables, Mathf.Max(minTables, maxTables) + 1);
        bool looseAmmo = rng.NextDouble() < looseAmmoChance;

        Chunk chunk = new Chunk
        {
            Root = root,
            Terrain = terrain,
            Rng = rng,
            TableCount = tableCount,
            LooseAmmo = looseAmmo
        };

        for (int i = 0; i < tableCount; i++)
            PlaceTableWithAmmo(root.transform, rng);

        if (looseAmmo)
            PlaceLooseAmmo(root.transform, rng);

        return chunk;
    }

    Terrain CreateTerrain(Vector3 origin, Transform parent)
    {
        int resolution = Mathf.Clamp(heightmapResolution, 33, 4097);
        resolution = Mathf.ClosestPowerOfTwo(resolution - 1) + 1;

        TerrainData data = new TerrainData
        {
            heightmapResolution = resolution,
            baseMapResolution = Mathf.Clamp(resolution / 4, 32, 512),
            size = new Vector3(chunkSize, terrainVerticalSize, chunkSize)
        };

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

        GameObject terrainGo = Terrain.CreateTerrainGameObject(data);
        terrainGo.name = "Terrain";
        terrainGo.transform.SetParent(parent, false);
        terrainGo.transform.position = origin;

        Terrain terrain = terrainGo.GetComponent<Terrain>();
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 5f;
        terrain.basemapDistance = 150f;

        return terrain;
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

    void PlaceTableWithAmmo(Transform parent, System.Random rng)
    {
        if (!tablePrefab) { return; }

        Vector3 pos = RandomPointInChunk(rng);
        Quaternion rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

        GameObject table = Instantiate(tablePrefab, pos, rot, parent);
        table.name = "Table";

        if (!placeAmmoOnTables || !ammoPrefab)
            return;

        Vector3 ammoPos = pos + Vector3.up * tableSurfaceOffset;
        ammoPos += new Vector3(
            ((float)rng.NextDouble() - 0.5f) * 0.4f,
            0f,
            ((float)rng.NextDouble() - 0.5f) * 0.4f);

        GameObject ammo = Instantiate(ammoPrefab, ammoPos, Quaternion.identity, parent);
        ammo.name = "AmmoOnTable";
    }

    void PlaceLooseAmmo(Transform parent, System.Random rng)
    {
        if (!ammoPrefab) { return; }

        Vector3 pos = RandomPointInChunk(rng) + Vector3.up * 0.2f;
        Instantiate(ammoPrefab, pos, Quaternion.identity, parent).name = "AmmoLoose";
    }

    Vector3 RandomPointInChunk(System.Random rng)
    {
        float min = edgePadding;
        float max = Mathf.Max(edgePadding, chunkSize - edgePadding);
        float x = Mathf.Lerp(min, max, (float)rng.NextDouble());
        float z = Mathf.Lerp(min, max, (float)rng.NextDouble());
        return new Vector3(x, GetHeightAt(x, z), z);
    }

    void DestroyChunk(Chunk chunk)
    {
        if (!chunk.Root)
            return;

        Terrain terrain = chunk.Terrain;
        if (terrain && terrain.terrainData)
            Destroy(terrain.terrainData);

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