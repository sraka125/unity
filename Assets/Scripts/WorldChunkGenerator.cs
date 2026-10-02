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
    
    [Header("Block / Cube Settings")]
    [Tooltip("Префаб обычного куба/блока для земли. Если не задан, создастся стандартный примитив Cube.")]
    [SerializeField] private GameObject blockPrefab;
    [SerializeField] private float blockSize = 2f; // Размер одного блока в метрах
    [SerializeField] private int blocksPerChunkAxis = 16; // Сколько блоков по X и Z в одном чанке
    [Tooltip("Отключить коллайдеры дочерних объектов блока — на одном блоке их может быть несколько, а объём уже покрыт корневым коллайдером.")]
    [SerializeField] private bool stripChildColliders = true;

    [Header("Setup")]
    [SerializeField] private GameObject[] disableOnStart;

    public event System.Action ChunksRebuilt;
    public Transform LocalPlayer => player;

    [Header("Terrain Shape (Cubic)")]
    [SerializeField] private float groundY = -23.44f;
    [SerializeField] private int seed = 1337;
    [SerializeField] private float noiseScale = 0.05f;
    [SerializeField] private float maxHeight = 16f; // Максимальная высота подъема блоков
    [SerializeField, Range(1, 8)] private int noiseOctaves = 3;
    [SerializeField, Range(1f, 3f)] private float lacunarity = 2f;
    [SerializeField, Range(0f, 1f)] private float gain = 0.5f;

    [Header("Props Per Chunk")]
    [SerializeField] private int minTables = 1;
    [SerializeField] private int maxTables = 3;
    [SerializeField] private float edgePadding = 2f;
    [SerializeField] private bool placeAmmoOnTables = true;
    [SerializeField, Range(0f, 1f)] private float looseAmmoChance = 0.35f;

    [Header("Ground Placement")]
    [SerializeField] private int placementAttempts = 32;
    [SerializeField] private float minTableSpacing = 4f;
    [SerializeField] private float propSink = 0f;

    [Header("Ammo Placement")]
    [SerializeField] private float ammoSurfaceOffset = 0.12f;
    [SerializeField] private float ammoSpread = 0.4f;

    readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();
    readonly List<Vector2Int> toRemove = new List<Vector2Int>();
    readonly List<Vector2Int> activeCoords = new List<Vector2Int>();

    Vector2Int lastCenter;
    bool hasCenter;
    bool initialized;
    bool setupDone;

    float chunkSize => blocksPerChunkAxis * blockSize;

    class Chunk
    {
        public GameObject Root;
        public System.Random Rng;
        public Vector3 Origin;
        public readonly List<Vector3> Tables = new List<Vector3>();
        public readonly List<GameObject> NetProps = new List<GameObject>();
        // Храним высоты для спавна предметов: ключ — (localX, localZ), значение — высота Y
        public readonly Dictionary<Vector2Int, float> HeightGrid = new Dictionary<Vector2Int, float>();
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
        }

        Vector3 centerWorldPos = Vector3.zero;
        Transform foundPlayer = ResolveLocalPlayer();
        
        if (foundPlayer)
        {
            player = foundPlayer;
            centerWorldPos = player.position;
        }

        lastCenter = WorldToChunk(centerWorldPos);
        hasCenter = true;
        RebuildChunks(lastCenter);

        initialized = true;
        return true;
    }

    Transform ResolveLocalPlayer()
    {
        if (player)
            return player;

        SUPERCharacterAIO character = FindFirstObjectByType<SUPERCharacterAIO>();
        return character ? character.transform : null;
    }

    void RebuildChunks(Vector2Int center)
    {
        activeCoords.Clear();
        int viewRadius = 2;

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
        return GetBlockTopAt(worldX, worldZ);
    }

    void GetBlockIndex(Vector2Int coord, float worldX, float worldZ, out int bx, out int bz)
    {
        Vector3 origin = ChunkOrigin(coord);

        bx = Mathf.Clamp(Mathf.FloorToInt((worldX - origin.x) / blockSize), 0, blocksPerChunkAxis - 1);
        bz = Mathf.Clamp(Mathf.FloorToInt((worldZ - origin.z) / blockSize), 0, blocksPerChunkAxis - 1);
    }

    int GetBlockSteps(Vector2Int coord, int bx, int bz)
    {
        Vector3 origin = ChunkOrigin(coord);
        float sampleX = origin.x + bx * blockSize;
        float sampleZ = origin.z + bz * blockSize;

        return Mathf.RoundToInt((SampleHeightNormalized(sampleX, sampleZ) * maxHeight) / blockSize);
    }

    float GetBlockTopAt(float worldX, float worldZ)
    {
        Vector2Int coord = WorldToChunk(new Vector3(worldX, 0f, worldZ));

        if (chunks.TryGetValue(coord, out Chunk chunk) && chunk.HeightGrid.Count > 0)
        {
            GetBlockIndex(coord, worldX, worldZ, out int bx, out int bz);

            if (chunk.HeightGrid.TryGetValue(new Vector2Int(bx, bz), out float top))
                return top;
        }

        GetBlockIndex(coord, worldX, worldZ, out int fx, out int fz);
        int steps = GetBlockSteps(coord, fx, fz);

        return groundY + steps * blockSize + blockSize * 0.5f;
    }

    Chunk CreateChunk(Vector2Int coord)
    {
        Vector3 origin = ChunkOrigin(coord);
        GameObject root = new GameObject($"CubicChunk_{coord.x}_{coord.y}");
        root.transform.SetParent(transform, false);

        Chunk chunk = new Chunk
        {
            Root = root,
            Origin = origin
        };

        System.Random rng = new System.Random(HashCoord(coord));
        chunk.Rng = rng;

        // Генерируем кубический рельеф
        for (int x = 0; x < blocksPerChunkAxis; x++)
        {
            for (int z = 0; z < blocksPerChunkAxis; z++)
            {
                int steps = GetBlockSteps(coord, x, z);
                float centerY = origin.y + steps * blockSize;
                float topY = centerY + blockSize * 0.5f;

                // HeightGrid хранит именно верхнюю грань — по ней ставятся игроки и предметы
                chunk.HeightGrid[new Vector2Int(x, z)] = topY;

                // Спавним верхний блок (или колонку блоков при желании)
                SpawnBlock(new Vector3(origin.x + x * blockSize + blockSize * 0.5f, centerY, origin.z + z * blockSize + blockSize * 0.5f), root.transform);
            }
        }

        int tableCount = rng.Next(minTables, Mathf.Max(minTables, maxTables) + 1);
        bool looseAmmo = rng.NextDouble() < looseAmmoChance;

        for (int i = 0; i < tableCount; i++)
            PlaceTableWithAmmo(chunk, rng);

        if (looseAmmo)
            PlaceLooseAmmo(chunk, rng);

        return chunk;
    }

    void SpawnBlock(Vector3 pos, Transform parent)
    {
        GameObject block;
        if (blockPrefab)
        {
            block = Instantiate(blockPrefab, pos, Quaternion.identity, parent);
            block.transform.localScale = new Vector3(blockSize, blockSize, blockSize);
        }
        else
        {
            block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.transform.position = pos;
            block.transform.localScale = new Vector3(blockSize, blockSize, blockSize);
            block.transform.SetParent(parent, false);
        }

        if (stripChildColliders)
            StripChildColliders(block);
    }

    void StripChildColliders(GameObject block)
    {
        Collider root = block.GetComponent<Collider>();

        if (!root)
            return;

        Collider[] all = block.GetComponentsInChildren<Collider>();

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != root && all[i].enabled)
                all[i].enabled = false;
        }
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

            sum += n * amplitude;
            norm += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return norm > 0f ? sum / norm : 0f;
    }

    void PlaceTableWithAmmo(Chunk chunk, System.Random rng)
    {
        if (!tablePrefab) return;

        if (!TryFindGroundSpot(chunk.Origin, chunk, rng, minTableSpacing, out Vector3 pos))
            return;

        // Спавним стол ровно на поверхности куба
        GameObject table = Instantiate(tablePrefab, pos, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), chunk.Root.transform);
        chunk.Tables.Add(pos);

        if (!placeAmmoOnTables) return;

        GameObject ammoToUse = ammoOnTablePrefab ? ammoOnTablePrefab : ammoPrefab;
        if (!ammoToUse) return;

        // Вычисляем верхнюю точку стола для спавна патронов прямо на нём
        float topY = GetHighestPoint(table, pos);
        Vector3 ammoPos = new Vector3(
            pos.x + ((float)rng.NextDouble() - 0.5f) * ammoSpread,
            topY + ammoSurfaceOffset,
            pos.z + ((float)rng.NextDouble() - 0.5f) * ammoSpread);

        Instantiate(ammoToUse, ammoPos, Quaternion.identity, chunk.Root.transform);
    }

    void PlaceLooseAmmo(Chunk chunk, System.Random rng)
    {
        if (!ammoPrefab) return;

        if (!TryFindGroundSpot(chunk.Origin, chunk, rng, 1.5f, out Vector3 pos))
            return;

        Vector3 ammoPos = new Vector3(pos.x, pos.y + ammoSurfaceOffset, pos.z);
        Instantiate(ammoPrefab, ammoPos, Quaternion.identity, chunk.Root.transform);
    }

    bool TryFindGroundSpot(Vector3 origin, Chunk chunk, System.Random rng, float spacing, out Vector3 spot)
    {
        spot = Vector3.zero;
        float min = edgePadding;
        float max = Mathf.Max(edgePadding, chunkSize - edgePadding);

        for (int attempt = 0; attempt < placementAttempts; attempt++)
        {
            float localX = Mathf.Lerp(min, max, (float)rng.NextDouble());
            float localZ = Mathf.Lerp(min, max, (float)rng.NextDouble());
            float worldX = origin.x + localX;
            float worldZ = origin.z + localZ;

            if (spacing > 0f && IsCrowded(chunk, worldX, worldZ, spacing))
                continue;

            // Строго берем точную высоту верхней грани куба под этой точкой
            float height = GetHeightAt(worldX, worldZ);

            spot = new Vector3(worldX, height + propSink, worldZ);
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

    void DestroyChunk(Chunk chunk)
    {
        if (chunk.Root)
            Destroy(chunk.Root);
    }

    public Vector3 GetTerrainNormalAt(float worldX, float worldZ, float radius)
    {
        return Vector3.up; // Для кубического мира нормаль всегда вверх
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