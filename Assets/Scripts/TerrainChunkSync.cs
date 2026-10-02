using Mirror;
using UnityEngine;

[RequireComponent(typeof(NetworkIdentity))]
public class TerrainChunkSync : NetworkBehaviour
{
    [SyncVar] private int coordX;
    [SyncVar] private int coordZ;

    private WorldChunkGenerator generator;

    public Vector2Int Coord => new Vector2Int(coordX, coordZ);

    public override void OnStartClient()
    {
        if (!isServer)
            RebuildHeightmap();
    }

    public void SetCoord(Vector2Int coord)
    {
        coordX = coord.x;
        coordZ = coord.y;
        RebuildHeightmap();
    }

    private void RebuildHeightmap()
    {
        if (!generator)
            generator = FindFirstObjectByType<WorldChunkGenerator>();

        if (!generator)
            return;

        Terrain terrain = GetComponent<Terrain>();

        if (terrain)
            generator.FillTerrain(terrain, Coord);
    }
}
