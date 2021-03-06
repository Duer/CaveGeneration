﻿using UnityEngine;
using System.Collections.Generic;
using System;

public enum TileType { Empty, Wall }

public class MapGenerator : MonoBehaviour
{
    #region Public Variables
    public int width = 64;
    public int height = 36;

    public string seed;                     //随机种子。
    public bool useRandomSeed;

    [Range(0, 100)]
    public int randomFillPercent = 45;      //随机填充百分比，越大洞越小。

    [Range(0, 20)]
    public int smoothLevel = 4;             //平滑程度。

    public int wallThresholdSize = 50;      //清除小墙体的阈值。
    public int roomThresholdSize = 50;      //清除小孔的的阈值。

    public int passageWidth = 4;            //通道（房间与房间直接）宽度。

    public int borderSize = 1;

    public bool showGizmos;
    #endregion

    private TileType[,] map;                     //地图集，Empty为空洞，Wall为实体墙。

    readonly int[,] upDownLeftRight = new int[4, 2] { { 0, 1 }, { 0, -1 }, { -1, 0 }, { 1, 0 } };

    //存放最后实际有效的空洞房间。
    private List<Room> survivingRooms = new List<Room>();

    private void Start()
    {
        GenerateMap();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            survivingRooms.Clear();
            GenerateMap();
        }
    }

    //生成随机地图。
    private void GenerateMap()
    {
        map = new TileType[width, height];
        RandomFillMap();

        for (int i = 0; i < smoothLevel; i++)
            SmoothMap();

        //清除小洞，小墙。
        ProcessMap();

        //连接各个幸存房间。
        ConnectClosestRooms(survivingRooms);

        //渲染地图。
        MeshGenerator meshGen = GetComponent<MeshGenerator>();
        meshGen.GenerateMesh(CrateStaticBorder(), 1);
    }

    //随机填充地图。
    private void RandomFillMap()
    {
        if (useRandomSeed)
            seed = Time.time.ToString();

        System.Random pseudoRandom = new System.Random(seed.GetHashCode());

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                    map[x, y] = TileType.Wall;
                else
                    map[x, y] = (pseudoRandom.Next(0, 100) < randomFillPercent) ? TileType.Wall : TileType.Empty;
    }

    //平滑地图
    private void SmoothMap()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int neighbourWallTiles = GetSurroundingWallCount(x, y);

                if (neighbourWallTiles > 4)             //周围大于四个实体墙，那自己也实体墙了。
                    map[x, y] = TileType.Wall;
                else if (neighbourWallTiles < 4)        //周围大于四个为空洞，那自己也空洞了。
                    map[x, y] = TileType.Empty;
                //还有如果四四开，那就保持不变。
            }
    }

    //获取该点周围8个点为实体墙（map[x,y] == 1）的个数。
    private int GetSurroundingWallCount(int gridX, int gridY)
    {
        int wallCount = 0;
        for (int neighbourX = gridX - 1; neighbourX <= gridX + 1; neighbourX++)
            for (int neighbourY = gridY - 1; neighbourY <= gridY + 1; neighbourY++)
                if (neighbourX >= 0 && neighbourX < width && neighbourY >= 0 && neighbourY < height)
                {
                    if (neighbourX != gridX || neighbourY != gridY)
                        wallCount += map[neighbourX, neighbourY] == TileType.Wall ? 1 : 0;
                }
                else
                    wallCount++;

        return wallCount;
    }

    //加工地图，清除小洞，小墙，连接房间。
    private void ProcessMap()
    {
        //获取最大房间的索引
        int currentIndex = 0, maxIndex = 0, maxSize = 0;
        //获取墙区域
        List<List<Coord>> wallRegions = GetRegions(TileType.Wall);
        foreach (List<Coord> wallRegion in wallRegions)
            if (wallRegion.Count < wallThresholdSize)
                foreach (Coord tile in wallRegion)
                    map[tile.tileX, tile.tileY] = TileType.Empty;                //把小于阈值的都铲掉。


        //获取空洞区域
        List<List<Coord>> roomRegions = GetRegions(TileType.Empty);
        foreach (List<Coord> roomRegion in roomRegions)
        {
            if (roomRegion.Count < roomThresholdSize)
                foreach (Coord tile in roomRegion)
                    map[tile.tileX, tile.tileY] = TileType.Wall;                //把小于阈值的都填充。
            else
            {
                survivingRooms.Add(new Room(roomRegion, map));      //添加到幸存房间列表里。
                if (maxSize < roomRegion.Count)
                {
                    maxSize = roomRegion.Count;
                    maxIndex = currentIndex;                        //找出最大房间的索引。
                }
                ++currentIndex;
            }
        }

        if (survivingRooms.Count == 0)
            Debug.LogError("No Survived Rooms Here!!");
        else
        {
            survivingRooms[maxIndex].isMainRoom = true;                 //最大房间就是主房间。
            survivingRooms[maxIndex].isAccessibleFromMainRoom = true;
        }
    }

    //获取区域
    private List<List<Coord>> GetRegions(TileType tileType)
    {
        List<List<Coord>> regions = new List<List<Coord>>();
        bool[,] mapFlags = new bool[width, height];

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (mapFlags[x, y] == false && map[x, y] == tileType)
                    regions.Add(GetRegionTiles(x, y, tileType, ref mapFlags));

        return regions;
    }

    //从这个点开始获取区域，广度优先算法。
    private List<Coord> GetRegionTiles(int startX, int startY, TileType tileType, ref bool[,] mapFlags)
    {
        List<Coord> tiles = new List<Coord>();
        Queue<Coord> queue = new Queue<Coord>();
        queue.Enqueue(new Coord(startX, startY));
        mapFlags[startX, startY] = true;

        while (queue.Count > 0)
        {
            Coord tile = queue.Dequeue();                       //弹出队列第一个，添加到要返回的列表里面。
            tiles.Add(tile);

            // 遍历上下左右四格
            for (int i = 0; i < 4; i++)
            {
                int x = tile.tileX + upDownLeftRight[i, 0];
                int y = tile.tileY + upDownLeftRight[i, 1];
                if (IsInMapRange(x, y) && mapFlags[x, y] == false && map[x, y] == tileType)
                {
                    mapFlags[x, y] = true;
                    queue.Enqueue(new Coord(x, y));
                }
            }
        }

        return tiles;
    }

    //连接各个房间。每个房间两两比较，找到最近房间（相对前一个房间）连接之，对第二个房间来说不一定就是最近的。
    //第二个参数为False时，第一步操作：为所有房间都连接到最近房间。
    //第二个参数为True时，第二步操作：就是把所有房间都连接到主房间。
    private void ConnectClosestRooms(List<Room> allRooms, bool forceAccessibilityFromMainRoom = false)
    {
        #region 属于第二步操作：roomListA 是还没连接到主房间的房间队列， roomListB 是已经连接到房间B的队列。
        List<Room> roomListA = new List<Room>();
        List<Room> roomListB = new List<Room>();

        if (forceAccessibilityFromMainRoom)                         //是否需要强制连接（直接或间接）到主房间。
        {
            foreach (Room room in allRooms)
                if (room.isAccessibleFromMainRoom)
                    roomListB.Add(room);                            //已经连接到主房间的加到ListB。
                else
                    roomListA.Add(room);                            //没有连接到主房间的加到ListA。为空时将结束递归。
        }
        else
        {
            roomListA = allRooms;
            roomListB = allRooms;
        }
        #endregion

        int bestDistance = 0;
        Coord bestTileA = new Coord();
        Coord bestTileB = new Coord();
        Room bestRoomA = new Room();
        Room bestRoomB = new Room();
        bool possibleConnectionFound = false;

        foreach (Room roomA in roomListA)                           //遍历没连接到主房间的ListA。
        {
            if (!forceAccessibilityFromMainRoom)                    //第一步：如果没有要求连到主房间。
            {
                possibleConnectionFound = false;                    //那就不能完成连接任务，需要不止一次连接。
                if (roomA.connectedRooms.Count > 0)                 //有连接房间，跳过，继续找下一个连接房间。
                    continue;
            }
            #region 遍历roomListB，找到距离当前roomA最近的roomB。
            foreach (Room roomB in roomListB)
            {
                if (roomA == roomB || roomA.IsConnected(roomB))
                    continue;

                for (int tileIndexA = 0; tileIndexA < roomA.edgeTiles.Count; tileIndexA++)
                    for (int tileIndexB = 0; tileIndexB < roomB.edgeTiles.Count; tileIndexB++)
                    {
                        Coord tileA = roomA.edgeTiles[tileIndexA];
                        Coord tileB = roomB.edgeTiles[tileIndexB];
                        int distanceBetweenRooms = (int)tileA.SqrMagnitude(tileB);

                        //如果找到更近的（相对roomA）房间，更新最短路径。
                        if (distanceBetweenRooms < bestDistance || !possibleConnectionFound)
                        {
                            bestDistance = distanceBetweenRooms;
                            possibleConnectionFound = true;
                            bestTileA = tileA;
                            bestTileB = tileB;
                            bestRoomA = roomA;
                            bestRoomB = roomB;
                        }
                    }
            }
            #endregion
            //第一步：找到新的两个连接房间,但是没有要求连接主房间。创建通道。
            if (possibleConnectionFound && !forceAccessibilityFromMainRoom)
                CreatePassage(bestRoomA, bestRoomB, bestTileA, bestTileB);
        }

        //第一步到第二步：当连接完所有房间，但是还没有要求全部连接到主房间，那就开始连接到主房间。
        if (!forceAccessibilityFromMainRoom)
            ConnectClosestRooms(allRooms, true);

        //第二步：当成功找到能连接到主房间，通路，继续找一下个能需要连到主房间的房间。
        if (possibleConnectionFound && forceAccessibilityFromMainRoom)
        {
            CreatePassage(bestRoomA, bestRoomB, bestTileA, bestTileB);
            ConnectClosestRooms(allRooms, true);
        }
    }

    //创建两个房间的通道。
    private void CreatePassage(Room roomA, Room roomB, Coord tileA, Coord tileB)
    {
        Room.ConnectRooms(roomA, roomB);
        //Debug.DrawLine(CoordToWorldPoint(tileA), CoordToWorldPoint(tileB), Color.green, 100);

        List<Coord> line = GetLine(tileA, tileB);
        foreach (Coord coord in line)
            DrawCircle(coord, passageWidth);
    }

    //获取两点直接线段经过的点。
    private List<Coord> GetLine(Coord from, Coord to)
    {
        List<Coord> line = new List<Coord>();

        int x = from.tileX;
        int y = from.tileY;

        int dx = to.tileX - from.tileX;
        int dy = to.tileY - from.tileY;

        bool inverted = false;
        int step = Math.Sign(dx);
        int gradientStep = Math.Sign(dy);

        int longest = Mathf.Abs(dx);
        int shortest = Mathf.Abs(dy);

        if (longest < shortest)
        {
            inverted = true;
            longest = Mathf.Abs(dy);
            shortest = Mathf.Abs(dx);

            step = Math.Sign(dy);
            gradientStep = Math.Sign(dx);
        }

        int gradientAccumulation = longest / 2;         //梯度积累，最长边的一半。
        for (int i = 0; i < longest; i++)
        {
            line.Add(new Coord(x, y));

            if (inverted)
                y += step;
            else
                x += step;

            gradientAccumulation += shortest;           //梯度每次增长为短边的长度。
            if (gradientAccumulation >= longest)
            {
                if (inverted)
                    x += gradientStep;
                else
                    y += gradientStep;
                gradientAccumulation -= longest;
            }
        }

        return line;
    }

    //以点c为原点，r为半径，画圈（拆墙）。
    private void DrawCircle(Coord c, int r)
    {
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                if (x * x + y * y <= r * r)
                {
                    int drawX = c.tileX + x;
                    int drawY = c.tileY + y;
                    if (IsInMapRange(drawX, drawY))
                        map[drawX, drawY] = TileType.Empty;
                }
    }

    //把xy坐标转换成实际坐标。
    private Vector3 CoordToWorldPoint(Coord tile)
    {
        return new Vector3(-width / 2 + .5f + tile.tileX, 2, -height / 2 + .5f + tile.tileY);
    }

    //判断坐标是否在地图里，不管墙还是洞。
    private bool IsInMapRange(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    //创建额外边界，这边界不参与任何地图计算
    private TileType[,] CrateStaticBorder()
    {
        TileType[,] borderedMap = new TileType[width + borderSize * 2, height + borderSize * 2];

        for (int x = 0; x < borderedMap.GetLength(0); x++)
            for (int y = 0; y < borderedMap.GetLength(1); y++)
                if (x >= borderSize && x < width + borderSize && y >= borderSize && y < height + borderSize)
                    borderedMap[x, y] = map[x - borderSize, y - borderSize];
                else
                    borderedMap[x, y] = TileType.Wall;
        return borderedMap;
    }

    private void OnDrawGizmos()
    {
        if (showGizmos && map != null)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Gizmos.color = (map[x, y] == TileType.Wall) ? new Color(0, 0, 0, 1f) : new Color(1, 1, 1, 1f);
                    Vector3 pos = new Vector3(-width / 2 + x + .5f, 0, -height / 2 + y + .5f);
                    pos.y = pos.y + 2;
                    Gizmos.DrawCube(pos, Vector3.one);
                }
            }
        }
    }


}