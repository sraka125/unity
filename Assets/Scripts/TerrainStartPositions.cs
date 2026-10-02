using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class TerrainStartPositions : MonoBehaviour
{
    [SerializeField] private WorldChunkGenerator generator;
    [SerializeField] private float heightOffset = 0.15f;
    [SerializeField] private bool alignToTerrainNormal = true;
    [SerializeField] private float normalSampleRadius = 1f;
    [SerializeField] private bool includeInactiveObjects = true;
    [SerializeField] private float repeatInterval = 0f;
    [SerializeField] private bool createDefaultIfMissing = true;
    [SerializeField] private int defaultStartCount = 2;
    [SerializeField] private float defaultSpawnSpacing = 3f;

    private readonly List<Transform> snapped = new List<Transform>();
    private float nextRepeat;

    public int SnappedCount => snapped.Count;

    void Start()
    {
        if (!generator)
            generator = FindFirstObjectByType<WorldChunkGenerator>();

        if (generator)
            generator.ChunksRebuilt += SnapAll;

        SnapAll();
        ScheduleRepeat();
    }

    void OnDestroy()
    {
        if (generator)
            generator.ChunksRebuilt -= SnapAll;
    }

    void Update()
    {
        if (repeatInterval <= 0f || Time.time < nextRepeat)
            return;

        ScheduleRepeat();
        SnapAll();
    }

    void ScheduleRepeat()
    {
        nextRepeat = repeatInterval > 0f ? Time.time + repeatInterval : float.PositiveInfinity;
    }

    public void SnapAll()
    {
        if (!generator)
            generator = FindFirstObjectByType<WorldChunkGenerator>();

        if (!generator)
            return;

        EnsureStartPositionsExist();

        snapped.Clear();

        NetworkStartPosition[] markers = FindObjectsByType<NetworkStartPosition>(
            includeInactiveObjects ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < markers.Length; i++)
            Snap(markers[i].transform);

        List<Transform> registered = NetworkManager.startPositions;

        if (registered != null)
        {
            for (int i = 0; i < registered.Count; i++)
                Snap(registered[i]);
        }
    }

    void EnsureStartPositionsExist()
    {
        List<Transform> registered = NetworkManager.startPositions;

        if (registered == null)
            return;

        registered.RemoveAll(t => t == null);

        if (!createDefaultIfMissing || registered.Count > 0)
            return;

        Vector3 anchor = generator.LocalPlayer ? generator.LocalPlayer.position : transform.position;
        int count = Mathf.Max(1, defaultStartCount);

        for (int i = 0; i < count; i++)
        {
            float offset = count > 1 ? Mathf.Lerp(-defaultSpawnSpacing, defaultSpawnSpacing, i / (float)(count - 1)) : 0f;

            GameObject startGo = new GameObject($"NetworkStartPosition_Auto_{i}");
            startGo.transform.SetParent(transform, false);
            startGo.transform.position = new Vector3(anchor.x + offset, anchor.y, anchor.z);

            startGo.AddComponent<NetworkStartPosition>();
        }
    }

    void Snap(Transform target)
    {
        if (!target)
            return;

        Vector3 position = target.position;
        float height = generator.GetHeightAt(position.x, position.z);

        if (height == float.NegativeInfinity)
            return;

        position.y = height + heightOffset;
        target.position = position;

        if (alignToTerrainNormal)
        {
            Vector3 normal = generator.GetTerrainNormalAt(position.x, position.z, normalSampleRadius);

            if (normal.sqrMagnitude > 0.5f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(target.forward, normal).normalized;

                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;

                target.rotation = Quaternion.LookRotation(forward, normal);
            }
        }

        snapped.Add(target);
    }
}

