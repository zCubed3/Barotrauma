﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.RuinGeneration;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
	public partial class Level : Entity, IServerSerializable
    {
        //all entities are disabled after they reach this depth
        public const int MaxEntityDepth = -300000;
        public const float ShaftHeight = 1000.0f;
        /// <summary>
        /// The level generator won't try to adjust the width of the main path above this limit.
        /// </summary>
        public const int MaxSubmarineWidth = 16000;

        public static Level Loaded { get; private set; }

        [Flags]
        public enum PositionType
        {
            MainPath = 0x1,
            SidePath = 0x2,
            Cave = 0x4,
            Ruin = 0x8,
            Wreck = 0x10,
            BeaconStation = 0x20
        }

        public struct InterestingPosition
        {
            public Point Position;
            public readonly PositionType PositionType;
            public bool IsValid;
            public Submarine Submarine;
            public Ruin Ruin;
            public Cave Cave;

            public InterestingPosition(Point position, PositionType positionType, Submarine submarine = null, bool isValid = true)
            {
                Position = position;
                PositionType = positionType;
                IsValid = isValid;
                Submarine = submarine;
                Ruin = null;
                Cave = null;
            }

            public InterestingPosition(Point position, PositionType positionType, Ruin ruin, bool isValid = true)
            {
                Position = position;
                PositionType = positionType;
                IsValid = isValid;
                Submarine = null;
                Ruin = ruin;
                Cave = null;
            }
            public InterestingPosition(Point position, PositionType positionType, Cave cave, bool isValid = true)
            {
                Position = position;
                PositionType = positionType;
                IsValid = isValid;
                Submarine = null;
                Ruin = null;
                Cave = cave;
            }
        }

        public enum TunnelType
        {
            MainPath, SidePath, Cave
        }

        public class Tunnel
        {
            public readonly Tunnel ParentTunnel;

            public readonly int MinWidth;

            public readonly TunnelType Type;

            public List<Point> Nodes
            {
                get;
                private set;
            }

            public List<VoronoiCell> Cells
            {
                get;
                private set;
            }

            public List<WayPoint> WayPoints
            {
                get;
                private set;
            }

            public Tunnel(TunnelType type, List<Point> nodes, int minWidth, Tunnel parentTunnel)
            {
                Type = type;
                MinWidth = minWidth;
                ParentTunnel = parentTunnel;
                Nodes = new List<Point>(nodes);
                Cells = new List<VoronoiCell>();
                WayPoints = new List<WayPoint>();
            }
        }

        public class Cave
        {
            public Rectangle Area;

            public readonly List<Tunnel> Tunnels = new List<Tunnel>();

            public Point StartPos, EndPos;

            public bool DisplayOnSonar;

            public readonly CaveGenerationParams CaveGenerationParams;

            public Cave(CaveGenerationParams caveGenerationParams, Rectangle area, Point startPos, Point endPos)
            {
                CaveGenerationParams = caveGenerationParams;
                Area = area;
                StartPos = startPos;
                EndPos = endPos;
            }
        }

        //how close the sub has to be to start/endposition to exit
        public const float ExitDistance = 6000.0f;
        public const int GridCellSize = 2000;
        private List<VoronoiCell>[,] cellGrid;
        private List<VoronoiCell> cells;

        //TODO: make private
        public List<double> siteCoordsX, siteCoordsY;

        //TODO: make private
        public List<Pair<Point, double>> distanceField;

        private Point startPosition, endPosition;

        private readonly Rectangle borders;

        private List<Body> bodies;

        private List<Point> bottomPositions;

        //no need for frequent network updates, as currently the only thing that's synced
        //are the slowly moving ice chunks that move in a very predictable way
        const float NetworkUpdateInterval = 5.0f;
        private float networkUpdateTimer;

        public Vector2 StartPosition
        {
            get { return startPosition.ToVector2(); }
        }

        public Point Size
        {
            get { return LevelData.Size; }
        }

        public Vector2 EndPosition
        {
            get { return endPosition.ToVector2(); }
        }

        public int BottomPos
        {
            get;
            private set;
        }

        public int SeaFloorTopPos
        {
            get;
            private set;
        }

        public const float DefaultRealWorldCrushDepth = 3500.0f;

        public float CrushDepth
        {
            get
            {
                return LevelData.CrushDepth;
            }
        }

        public float RealWorldCrushDepth
        {
            get
            {
                return LevelData.RealWorldCrushDepth;
            }
        }

        public LevelWall SeaFloor { get; private set; }

        public List<Ruin> Ruins { get; private set; }

        public List<Submarine> Wrecks { get; private set; }

        public Submarine BeaconStation { get; private set; }
        private Sonar beaconSonar;

        public List<LevelWall> ExtraWalls { get; private set; }

        public List<LevelWall> UnsyncedExtraWalls { get; private set; }

        public List<Tunnel> Tunnels { get; private set; } = new List<Tunnel>();

        public List<Cave> Caves { get; private set; } = new List<Cave>();

        public List<InterestingPosition> PositionsOfInterest { get; private set; }

        public Submarine StartOutpost { get; private set; }
        public Submarine EndOutpost { get; private set; }

        private SubmarineInfo preSelectedStartOutpost;
        private SubmarineInfo preSelectedEndOutpost;

        public readonly LevelData LevelData;

        /// <summary>
        /// Random integers generated during the level generation. If these values differ between clients/server,
        /// it means the levels aren't identical for some reason and there will most likely be major ID mismatches.
        /// </summary>
        public List<int> EqualityCheckValues
        {
            get;
            private set;
        } = new List<int>();

        public List<Entity> EntitiesBeforeGenerate { get; private set; } = new List<Entity>();
        public int EntityCountBeforeGenerate { get; private set; }
        public int EntityCountAfterGenerate { get; private set; }

        public Body TopBarrier
        {
            get;
            private set;
        }

        public Body BottomBarrier
        {
            get;
            private set;
        }

        public LevelObjectManager LevelObjectManager { get; private set; }

        public bool Generating { get; private set; }

        public Location StartLocation { get; private set; }
        public Location EndLocation { get; private set; }

        public bool Mirrored
        {
            get;
            private set;
        }

        public string Seed 
        {
            get { return LevelData.Seed; }
        }

        public float Difficulty
        {
            get { return LevelData.Difficulty; }
        }

        public LevelData.LevelType Type
        {
            get { return LevelData.Type; }
        }

        /// <summary>
        /// Is there a loaded level set and is it an outpost?
        /// </summary>
        public static bool IsLoadedOutpost => Loaded?.Type == LevelData.LevelType.Outpost;

        public LevelGenerationParams GenerationParams
        {
            get { return LevelData.GenerationParams; }
        }

        public Color BackgroundTextureColor
        {
            get { return LevelData.GenerationParams.BackgroundTextureColor; }
        }

        public Color BackgroundColor
        {
            get { return LevelData.GenerationParams.BackgroundColor; }
        }

        public Color WallColor
        {
            get { return LevelData.GenerationParams.WallColor; }
        }

        private Level(LevelData levelData) : base(null, 0)
        {
            this.LevelData = levelData;
            borders = new Rectangle(Point.Zero, levelData.Size);

            //remove from entity dictionary
            //base.Remove();
        }

        public static Level Generate(LevelData levelData, bool mirror, SubmarineInfo startOutpost = null, SubmarineInfo endOutpost = null)
        {
            Debug.Assert(levelData.Biome != null);
            if (levelData.Biome == null) { throw new ArgumentException("Biome was null"); }
            if (levelData.Size.X <= 0) { throw new ArgumentException("Level width needs to be larger than zero."); }
            if (levelData.Size.Y <= 0) { throw new ArgumentException("Level height needs to be larger than zero."); }

            Level level = new Level(levelData)
            {
                preSelectedStartOutpost = startOutpost,
                preSelectedEndOutpost = endOutpost
            };
            level.Generate(mirror);
            return level;
        }

        private void Generate(bool mirror)
        {
            if (Loaded != null) { Loaded.Remove(); }
            Loaded = this;
            Generating = true;

            EqualityCheckValues.Clear();
            EntitiesBeforeGenerate = GetEntities().ToList();
            EntityCountBeforeGenerate = EntitiesBeforeGenerate.Count();

            StartLocation = GameMain.GameSession?.StartLocation;
            EndLocation = GameMain.GameSession?.EndLocation;

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            LevelObjectManager = new LevelObjectManager();

            if (Type == LevelData.LevelType.Outpost) { mirror = false; }
            Mirrored = mirror;

#if CLIENT
            if (backgroundCreatureManager == null)
            {
                var files = GameMain.Instance.GetFilesOfType(ContentType.BackgroundCreaturePrefabs);
                if (files.Count() > 0)
                    backgroundCreatureManager = new BackgroundCreatureManager(files);
                else
                    backgroundCreatureManager = new BackgroundCreatureManager("Content/BackgroundCreatures/BackgroundCreaturePrefabs.xml");
            }
#endif
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            PositionsOfInterest = new List<InterestingPosition>();
            ExtraWalls = new List<LevelWall>();
            UnsyncedExtraWalls = new List<LevelWall>();
            bodies = new List<Body>();
            List<Vector2> sites = new List<Vector2>();
            
            Voronoi voronoi = new Voronoi(1.0);
            Rand.SetSyncedSeed(ToolBox.StringToInt(Seed));

#if CLIENT
            renderer = new LevelRenderer(this);
#endif

            SeaFloorTopPos = GenerationParams.SeaFloorDepth + GenerationParams.MountainHeightMax + GenerationParams.SeaFloorVariance;

            int minWidth = Math.Min(GenerationParams.MinTunnelRadius, MaxSubmarineWidth);
            if (Submarine.MainSub != null)
            {
                Rectangle dockedSubBorders = Submarine.MainSub.GetDockedBorders();
                dockedSubBorders.Inflate(dockedSubBorders.Size.ToVector2() * 0.15f);
                minWidth = Math.Max(minWidth, Math.Max(dockedSubBorders.Width, dockedSubBorders.Height));
                minWidth = Math.Min(minWidth, MaxSubmarineWidth);
            }
            minWidth = Math.Min(minWidth, borders.Width / 5);
            LevelData.MinMainPathWidth = minWidth;

            Rectangle pathBorders = borders;
            pathBorders.Inflate(
                -Math.Min(Math.Min(minWidth * 2, MaxSubmarineWidth), borders.Width / 5), 
                -Math.Min(minWidth, borders.Height / 5));

            if (pathBorders.Width <= 0) { throw new InvalidOperationException($"The width of the level's path area is invalid ({pathBorders.Width})"); }
            if (pathBorders.Height <= 0) { throw new InvalidOperationException($"The height of the level's path area is invalid ({pathBorders.Height})"); }

            startPosition = new Point(
               (int)MathHelper.Lerp(minWidth, borders.Width - minWidth, GenerationParams.StartPosition.X),
               (int)MathHelper.Lerp(borders.Bottom - minWidth, borders.Y + minWidth, GenerationParams.StartPosition.Y));
            endPosition = new Point(
               (int)MathHelper.Lerp(minWidth, borders.Width - minWidth, GenerationParams.EndPosition.X),
               (int)MathHelper.Lerp(borders.Bottom - minWidth, borders.Y + minWidth, GenerationParams.EndPosition.Y));

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            //generate the initial nodes for the main path and smaller tunnels
            //----------------------------------------------------------------------------------

            Tunnel mainPath = new Tunnel(
                TunnelType.MainPath, 
                GeneratePathNodes(startPosition, endPosition, pathBorders, null, GenerationParams.MainPathVariance), 
                minWidth, parentTunnel: null);
            Tunnels.Add(mainPath);

            int sideTunnelCount = Rand.Range(GenerationParams.SideTunnelCount.X, GenerationParams.SideTunnelCount.Y + 1, Rand.RandSync.Server);
            for (int j = 0; j < sideTunnelCount; j++)
            {
                if (mainPath.Nodes.Count < 4) { break; }
                var validTunnels = Tunnels.FindAll(t => t.Type != TunnelType.Cave);
                Tunnel tunnelToBranchOff = validTunnels[Rand.Int(validTunnels.Count, Rand.RandSync.Server)];
                if (tunnelToBranchOff == null) { tunnelToBranchOff = mainPath; }

                Point branchStart = tunnelToBranchOff.Nodes[Rand.Range(0, tunnelToBranchOff.Nodes.Count / 3, Rand.RandSync.Server)];
                Point branchEnd = tunnelToBranchOff.Nodes[Rand.Range(tunnelToBranchOff.Nodes.Count / 3 * 2, tunnelToBranchOff.Nodes.Count - 1, Rand.RandSync.Server)];

                var sidePathNodes = GeneratePathNodes(branchStart, branchEnd, pathBorders, tunnelToBranchOff, GenerationParams.SideTunnelVariance);
                //make sure the path is wide enough to pass through
                int pathWidth = Rand.Range(GenerationParams.MinSideTunnelRadius.X, GenerationParams.MinSideTunnelRadius.Y, Rand.RandSync.Server);
                Tunnels.Add(new Tunnel(TunnelType.SidePath, sidePathNodes, pathWidth, parentTunnel: tunnelToBranchOff));
            }

            CalculateTunnelDistanceField(density: 1000);
            GenerateSeaFloorPositions(mirror);
            GenerateCaves(mainPath);

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            //generate voronoi sites
            //----------------------------------------------------------------------------------
                        
            Point siteInterval = GenerationParams.VoronoiSiteInterval;
            int siteIntervalSqr = (siteInterval.X * siteInterval.X + siteInterval.Y * siteInterval.Y);
            Point siteVariance = GenerationParams.VoronoiSiteVariance;
            siteCoordsX = new List<double>((borders.Height / siteInterval.Y) * (borders.Width / siteInterval.Y));
            siteCoordsY = new List<double>((borders.Height / siteInterval.Y) * (borders.Width / siteInterval.Y));
            int caveSiteInterval = 500;
            for (int x = siteInterval.X / 2; x < borders.Width; x += siteInterval.X)
            {
                for (int y = siteInterval.Y / 2; y < borders.Height; y += siteInterval.Y)
                {
                    int siteX = x + Rand.Range(-siteVariance.X, siteVariance.X, Rand.RandSync.Server);
                    int siteY = y + Rand.Range(-siteVariance.Y, siteVariance.Y, Rand.RandSync.Server);

                    bool closeToTunnel = false;
                    bool closeToCave = false;
                    foreach (Tunnel tunnel in Tunnels)
                    {
                        for (int i = 1; i < tunnel.Nodes.Count; i++)
                        {
                            float minDist = Math.Max(tunnel.MinWidth * 2.0f, Math.Max(siteInterval.X, siteInterval.Y));
                            if (siteX < Math.Min(tunnel.Nodes[i - 1].X, tunnel.Nodes[i].X) - minDist) { continue; }
                            if (siteX > Math.Max(tunnel.Nodes[i - 1].X, tunnel.Nodes[i].X) + minDist) { continue; }
                            if (siteY < Math.Min(tunnel.Nodes[i - 1].Y, tunnel.Nodes[i].Y) - minDist) { continue; }
                            if (siteY > Math.Max(tunnel.Nodes[i - 1].Y, tunnel.Nodes[i].Y) + minDist) { continue; }

                            double tunnelDistSqr = MathUtils.LineSegmentToPointDistanceSquared(tunnel.Nodes[i - 1], tunnel.Nodes[i], new Point(siteX, siteY));
                            if (Math.Sqrt(tunnelDistSqr) < minDist)
                            {
                                closeToTunnel = true;
                                tunnelDistSqr = MathUtils.LineSegmentToPointDistanceSquared(tunnel.Nodes[i - 1], tunnel.Nodes[i], new Point(siteX, siteY));
                                if (tunnel.Type == TunnelType.Cave)
                                {
                                    closeToCave = true;
                                }
                                break;
                            }
                        }
                    }

                    if (!closeToTunnel) 
                    {
                        //make the graph less dense (90% less nodes) in areas far away from tunnels where we don't need a lot of geometry 
                        if (Rand.Range(0, 10, Rand.RandSync.Server) != 0) { continue; }
                    }

                    siteCoordsX.Add(siteX);
                    siteCoordsY.Add(siteY);

                    if (closeToCave)
                    {
                        for (int x2 = x; x2 < x + siteInterval.X; x2 += caveSiteInterval)
                        {
                            for (int y2 = y; y2 < y + siteInterval.Y; y2 += caveSiteInterval)
                            {
                                int caveSiteX = x2 + Rand.Int(caveSiteInterval / 2, Rand.RandSync.Server);
                                int caveSiteY = y2 + Rand.Int(caveSiteInterval / 2, Rand.RandSync.Server);

                                bool tooClose = false;
                                for (int i = 0; i < siteCoordsX.Count; i++)
                                {
                                    if (MathUtils.DistanceSquared(caveSiteX, caveSiteY, siteCoordsX[i], siteCoordsY[i]) < 10.0f * 10.0f)
                                    {
                                        tooClose = true;
                                        break;
                                    }
                                }
                                if (tooClose) { continue; }
                                siteCoordsX.Add(caveSiteX);
                                siteCoordsY.Add(caveSiteY);
                            }
                        }

                    }

                }
            }

            /*int caveSiteInterval = 500;
            foreach (Cave cave in Caves)
            {
                for (int x = cave.Area.X; x < cave.Area.Right; x += caveSiteInterval)
                {
                    for (int y = cave.Area.Y; y < cave.Area.Bottom; y += caveSiteInterval)
                    {
                        int siteX = x + Rand.Int(caveSiteInterval / 2);
                        int siteY = y + Rand.Int(caveSiteInterval / 2);

                        bool tooClose = false;
                        for (int i = 0; i<siteCoordsX.Count; i++)
                        {
                            if (MathUtils.DistanceSquared(siteX, siteY, siteCoordsX[i],siteCoordsY[i]) < 10.0f * 10.0f)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (tooClose) { continue; }
                        siteCoordsX.Add(siteX);
                        siteCoordsY.Add(siteY);
                    }                    
                }
            }*/

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // construct the voronoi graph and cells
            //----------------------------------------------------------------------------------

            Stopwatch sw2 = new Stopwatch();
            sw2.Start();

            Debug.Assert(siteCoordsX.Count == siteCoordsY.Count);
            List<GraphEdge> graphEdges = voronoi.MakeVoronoiGraph(siteCoordsX.ToArray(), siteCoordsY.ToArray(), borders.Width, borders.Height);

            Debug.WriteLine("MakeVoronoiGraph: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();
            
            //construct voronoi cells based on the graph edges
            cells = CaveGenerator.GraphEdgesToCells(graphEdges, borders, GridCellSize, out cellGrid);
            
            Debug.WriteLine("find cells: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            //----------------------------------------------------------------------------------
            // generate a path through the tunnel nodes
            //----------------------------------------------------------------------------------

            List<VoronoiCell> pathCells = new List<VoronoiCell>();
            foreach (Tunnel tunnel in Tunnels)
            {
                CaveGenerator.GeneratePath(tunnel, cells, cellGrid, GridCellSize, pathBorders);
                if (tunnel.Type == TunnelType.MainPath || tunnel.Type == TunnelType.SidePath)
                {
                    var distinctCells = tunnel.Cells.Distinct().ToList();
                    for (int i = 2; i < distinctCells.Count; i += 3)
                    {
                        PositionsOfInterest.Add(new InterestingPosition(
                            new Point((int)distinctCells[i].Site.Coord.X, (int)distinctCells[i].Site.Coord.Y),
                            tunnel.Type == TunnelType.MainPath ? PositionType.MainPath : PositionType.SidePath, 
                            Caves.Find(cave => cave.Tunnels.Contains(tunnel))));
                    }
                }
                GenerateWaypoints(tunnel, parentTunnel: tunnel.ParentTunnel);
                EnlargePath(tunnel.Cells, tunnel.MinWidth);
                foreach (var pathCell in tunnel.Cells)
                {
                    MarkEdges(pathCell, tunnel.Type);
                    foreach (GraphEdge edge in pathCell.Edges)
                    {
                        var adjacent = edge.AdjacentCell(pathCell);
                        if (adjacent != null)
                        {
                            MarkEdges(adjacent, tunnel.Type);
                        }
                    }
                    if (!pathCells.Contains(pathCell))
                    {
                        pathCells.Add(pathCell);
                    }
                }

                static void MarkEdges(VoronoiCell cell, TunnelType tunnelType)
                {
                    foreach (GraphEdge edge in cell.Edges)
                    {
                        switch (tunnelType)
                        {
                            case TunnelType.MainPath:
                                edge.NextToMainPath = true;
                                break;
                            case TunnelType.SidePath:
                                edge.NextToSidePath = true;
                                break;
                            case TunnelType.Cave:
                                edge.NextToCave = true;
                                break;
                        }
                    }
                }
            }

            var potentialIslands = new List<VoronoiCell>();
            foreach (var cell in pathCells)
            {
                if (GetDistToTunnel(cell.Center, mainPath) < minWidth) { continue; }
                if (cell.Edges.Any(e => e.AdjacentCell(cell)?.CellType != CellType.Path || e.NextToCave)) { continue; }
                potentialIslands.Add(cell);
            }
            for (int i = 0; i < GenerationParams.IslandCount; i++)
            {
                if (potentialIslands.Count == 0) { break; }
                var island = potentialIslands.GetRandom(Rand.RandSync.Server);
                island.CellType = CellType.Solid;
                island.Island = true;
                pathCells.Remove(island);
            }

            foreach (InterestingPosition positionOfInterest in PositionsOfInterest)
            {
                WayPoint wayPoint = new WayPoint(
                    positionOfInterest.Position.ToVector2(),
                    SpawnType.Enemy,
                    submarine: null);
            }

            startPosition.X = (int)pathCells[0].Site.Coord.X;

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // remove unnecessary cells and create some holes at the bottom of the level
            //----------------------------------------------------------------------------------

            cells = cells.Except(pathCells).ToList();
            //remove cells from the edges and bottom of the map because a clean-cut edge of the level looks bad
            cells.ForEachMod(c => 
            { 
                if (c.Edges.Any(e => !MathUtils.NearlyEqual(e.Point1.Y, Size.Y) && e.AdjacentCell(c) == null))
                {
                    c.CellType = CellType.Removed;
                    cells.Remove(c);
                }
            });

            int xPadding = borders.Width / 5;
            pathCells.AddRange(CreateHoles(GenerationParams.BottomHoleProbability, new Rectangle(xPadding, 0, borders.Width - xPadding * 2, Size.Y / 2), minWidth));

            foreach (VoronoiCell cell in cells)
            {
                if (cell.Site.Coord.Y < borders.Height / 2) { continue; }
                cell.Edges.ForEach(e => e.OutsideLevel = true);
            }

            //----------------------------------------------------------------------------------
            // initialize the cells that are still left and insert them into the cell grid
            //----------------------------------------------------------------------------------
            
            foreach (VoronoiCell cell in pathCells)
            {
                cell.Edges.ForEach(e => e.OutsideLevel = false);

                cell.CellType = CellType.Path;
                cells.Remove(cell);
            }
            
            for (int x = 0; x < cellGrid.GetLength(0); x++)
            {
                for (int y = 0; y < cellGrid.GetLength(1); y++)
                {
                    cellGrid[x, y].Clear();
                }
            }

            //----------------------------------------------------------------------------------
            // mirror if needed
            //----------------------------------------------------------------------------------
            
            if (mirror)
            {
                HashSet<GraphEdge> mirroredEdges = new HashSet<GraphEdge>();
                HashSet<Site> mirroredSites = new HashSet<Site>();
                List<VoronoiCell> allCells = new List<VoronoiCell>(cells);
                allCells.AddRange(pathCells);
                foreach (VoronoiCell cell in allCells)
                {
                    foreach (GraphEdge edge in cell.Edges)
                    {
                        if (mirroredEdges.Contains(edge)) { continue; }
                        edge.Point1.X = borders.Width - edge.Point1.X;
                        edge.Point2.X = borders.Width - edge.Point2.X;
                        if (edge.Site1 != null && !mirroredSites.Contains(edge.Site1))
                        {
                            //make sure that sites right at the edge of a grid cell end up in the same cell as in the non-mirrored level
                            if (edge.Site1.Coord.X % GridCellSize < 1.0f &&
                                edge.Site1.Coord.X % GridCellSize >= 0.0f) { edge.Site1.Coord.X += 1.0f; }
                            edge.Site1.Coord.X = borders.Width - edge.Site1.Coord.X;
                            mirroredSites.Add(edge.Site1);
                        }
                        if (edge.Site2 != null && !mirroredSites.Contains(edge.Site2))
                        {
                            if (edge.Site2.Coord.X % GridCellSize < 1.0f &&
                                edge.Site2.Coord.X % GridCellSize >= 0.0f) { edge.Site2.Coord.X += 1.0f; }
                            edge.Site2.Coord.X = borders.Width - edge.Site2.Coord.X;
                            mirroredSites.Add(edge.Site2);
                        }
                        mirroredEdges.Add(edge);
                    }
                }

                foreach (Cave cave in Caves)
                {
                    cave.Area = new Rectangle(borders.Width - cave.Area.Right, cave.Area.Y, cave.Area.Width, cave.Area.Height);
                    cave.StartPos = new Point(borders.Width - cave.StartPos.X, cave.StartPos.Y);
                    cave.EndPos = new Point(borders.Width - cave.EndPos.X, cave.EndPos.Y);
                }

                foreach (Tunnel tunnel in Tunnels)
                {
                    for (int i = 0; i < tunnel.Nodes.Count; i++)
                    {
                        tunnel.Nodes[i] = new Point(borders.Width - tunnel.Nodes[i].X, tunnel.Nodes[i].Y);
                    }
                }

                for (int i = 0; i < PositionsOfInterest.Count; i++)
                {
                    PositionsOfInterest[i] = new InterestingPosition(
                        new Point(borders.Width - PositionsOfInterest[i].Position.X, PositionsOfInterest[i].Position.Y),
                        PositionsOfInterest[i].PositionType)
                    {
                        Submarine = PositionsOfInterest[i].Submarine,
                        Cave = PositionsOfInterest[i].Cave,
                        Ruin = PositionsOfInterest[i].Ruin,
                    };
                }

                foreach (WayPoint waypoint in WayPoint.WayPointList)
                {
                    if (waypoint.Submarine != null) continue;
                    waypoint.Move(new Vector2((borders.Width / 2 - waypoint.Position.X) * 2, 0.0f));
                }

                startPosition.X = borders.Width - startPosition.X;
                endPosition.X = borders.Width - endPosition.X;
            }

            foreach (VoronoiCell cell in cells)
            {
                int x = (int)Math.Floor(cell.Site.Coord.X / GridCellSize);
                x = MathHelper.Clamp(x, 0, cellGrid.GetLength(0) - 1);
                int y = (int)Math.Floor(cell.Site.Coord.Y / GridCellSize);
                y = MathHelper.Clamp(y, 0, cellGrid.GetLength(1) - 1);

                cellGrid[x, y].Add(cell);
            }

            float destructibleWallRatio = MathHelper.Lerp(0.2f, 1.0f, LevelData.Difficulty / 100.0f);
            foreach (Cave cave in Caves)
            {
                CreatePathToClosestTunnel(cave.StartPos);

                List<VoronoiCell> caveCells = new List<VoronoiCell>();
                caveCells.AddRange(cave.Tunnels.SelectMany(t => t.Cells));
                foreach (var caveCell in caveCells)
                {
                    if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < destructibleWallRatio * cave.CaveGenerationParams.DestructibleWallRatio)
                    {
                        var chunk = CreateIceChunk(caveCell.Edges, caveCell.Center, health: 50.0f);
                        if (chunk != null)
                        {
                            chunk.Body.BodyType = BodyType.Static;
                            ExtraWalls.Add(chunk);
                        }
                    }
                }
            }

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // create some ruins
            //----------------------------------------------------------------------------------

            Ruins = new List<Ruin>();
            for (int i = 0; i < GenerationParams.RuinCount; i++)
            {
                GenerateRuin(mainPath, mirror);
            }

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // create floating ice chunks
            //----------------------------------------------------------------------------------

            if (GenerationParams.FloatingIceChunkCount > 0)
            {
                List<Point> iceChunkPositions = new List<Point>();
                foreach (InterestingPosition pos in PositionsOfInterest)
                {
                    if (pos.PositionType != PositionType.MainPath && pos.PositionType != PositionType.SidePath) { continue; }
                    if (pos.Position.X < 5000 || pos.Position.X > Size.X - 5000) { continue; }
                    if (Math.Abs(pos.Position.X - StartPosition.X) < minWidth * 2 || Math.Abs(pos.Position.X - EndPosition.X) < minWidth * 2) { continue; }
                    if (GetTooCloseCells(pos.Position.ToVector2(), minWidth * 0.7f).Count > 0) { continue; }
                    iceChunkPositions.Add(pos.Position);
                }
                        
                for (int i = 0; i < GenerationParams.FloatingIceChunkCount; i++)
                {
                    if (iceChunkPositions.Count == 0) { break; }
                    Point selectedPos = iceChunkPositions[Rand.Int(iceChunkPositions.Count, Rand.RandSync.Server)];
                    float chunkRadius = Rand.Range(500.0f, 1000.0f, Rand.RandSync.Server);
                    var vertices = CaveGenerator.CreateRandomChunk(chunkRadius, 8, chunkRadius * 0.8f);
                    var chunk = CreateIceChunk(vertices, selectedPos.ToVector2());
                    chunk.MoveAmount = new Vector2(0.0f, minWidth * 0.7f);
                    chunk.MoveSpeed = Rand.Range(100.0f, 200.0f, Rand.RandSync.Server);
                    ExtraWalls.Add(chunk);
                    iceChunkPositions.Remove(selectedPos);
                }
            }

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // generate the bodies and rendered triangles of the cells
            //----------------------------------------------------------------------------------

            foreach (VoronoiCell cell in cells)
            {
                foreach (GraphEdge ge in cell.Edges)
                {
                    VoronoiCell adjacentCell = ge.AdjacentCell(cell);
                    ge.IsSolid = adjacentCell == null || !cells.Contains(adjacentCell);
                }
            }

            List<VoronoiCell> cellsWithBody = new List<VoronoiCell>(cells);
            if (GenerationParams.CellRoundingAmount > 0.01f || GenerationParams.CellIrregularity > 0.01f)
            {
                foreach (VoronoiCell cell in cellsWithBody)
                {
                    CaveGenerator.RoundCell(cell,
                        minEdgeLength: GenerationParams.CellSubdivisionLength,
                        roundingAmount: GenerationParams.CellRoundingAmount,
                        irregularity: GenerationParams.CellIrregularity);
                }
            }


#if CLIENT
            List<Pair<List<VoronoiCell>, Cave>> cellBatches = new List<Pair<List<VoronoiCell>, Cave>>
            {
                new Pair<List<VoronoiCell>, Cave>(cellsWithBody.ToList(), null)
            };
            foreach (Cave cave in Caves)
            {
                var newCellBatch = new Pair<List<VoronoiCell>, Cave>(new List<VoronoiCell>(), cave);
                foreach (var caveCell in cave.Tunnels.SelectMany(t => t.Cells))
                {
                    foreach (var edge in caveCell.Edges)
                    {
                        if (!edge.NextToCave) { continue; }
                        if (edge.Cell1?.CellType == CellType.Solid && !newCellBatch.First.Contains(edge.Cell1)) 
                        {
                            cellBatches.ForEach(cb => cb.First.Remove(edge.Cell1));
                            newCellBatch.First.Add(edge.Cell1); 
                        }
                        if (edge.Cell2?.CellType == CellType.Solid && !newCellBatch.First.Contains(edge.Cell2))
                        {
                            cellBatches.ForEach(cb => cb.First.Remove(edge.Cell2));
                            newCellBatch.First.Add(edge.Cell2);
                        }
                    }
                }
                if (newCellBatch.First.Any())
                {
                    cellBatches.Add(newCellBatch);
                }
            }
            cellBatches.RemoveAll(cb => !cb.First.Any());

            Debug.Assert(cellsWithBody.Count == cellBatches.Sum(cb => cb.First.Count));

            List<List<Vector2[]>> triangleLists = new List<List<Vector2[]>>();
            foreach (Pair<List<VoronoiCell>, Cave> cellBatch in cellBatches)
            {
                bodies.Add(CaveGenerator.GeneratePolygons(cellBatch.First, this, out List<Vector2[]> triangles));
                triangleLists.Add(triangles);
            }
#else
            bodies.Add(CaveGenerator.GeneratePolygons(cellsWithBody, this, out List<Vector2[]> triangles));
#endif
            foreach (VoronoiCell cell in cells)
            {
                CompareCCW compare = new CompareCCW(cell.Center);
                foreach (GraphEdge edge in cell.Edges)
                {
                    //remove references to cells that we failed to generate a body for
                    if (edge.Cell1 != null && edge.Cell1.Body == null && edge.Cell1.CellType != CellType.Empty) { edge.Cell1 = null; }
                    if (edge.Cell2 != null && edge.Cell2.Body == null && edge.Cell2.CellType != CellType.Empty) { edge.Cell2 = null; }

                    //make the order of the points CCW
                    if (compare.Compare(edge.Point1, edge.Point2) == -1)
                    {
                        var temp = edge.Point1;
                        edge.Point1 = edge.Point2;
                        edge.Point2 = temp;
                    }
                }
            }

#if CLIENT
            Debug.Assert(triangleLists.Count == cellBatches.Count);
            for (int i = 0; i < triangleLists.Count; i++)
            {
                renderer.SetVertices(
                    CaveGenerator.GenerateWallVertices(triangleLists[i], GenerationParams, zCoord: 0.9f).ToArray(),
                    CaveGenerator.GenerateWallEdgeVertices(cellBatches[i].First, this, zCoord: 0.9f).ToArray(),
                    cellBatches[i].Second?.CaveGenerationParams?.WallSprite == null ? GenerationParams.WallSprite.Texture : cellBatches[i].Second.CaveGenerationParams.WallSprite.Texture,
                    cellBatches[i].Second?.CaveGenerationParams?.WallEdgeSprite == null ? GenerationParams.WallEdgeSprite.Texture : cellBatches[i].Second.CaveGenerationParams.WallEdgeSprite.Texture,
                    GenerationParams.WallColor);
            }
#endif


            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // create ice spires
            //----------------------------------------------------------------------------------

            List<GraphEdge> usedSpireEdges = new List<GraphEdge>();
            for (int i = 0; i < GenerationParams.IceSpireCount; i++)
            {
                var spire = CreateIceSpire(usedSpireEdges);
                if (spire != null) { ExtraWalls.Add(spire); };
            }

            //----------------------------------------------------------------------------------
            // connect side paths and cave branches to their parents
            //----------------------------------------------------------------------------------

            foreach (Tunnel tunnel in Tunnels)
            {
                if (tunnel.ParentTunnel == null) { continue; }
                if (tunnel.Type == TunnelType.Cave && tunnel.ParentTunnel == mainPath) { continue; }
                ConnectWaypoints(tunnel, tunnel.ParentTunnel);
            }

            //----------------------------------------------------------------------------------
            // create outposts at the start and end of the level
            //----------------------------------------------------------------------------------

            CreateOutposts();

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // top barrier & sea floor
            //----------------------------------------------------------------------------------

            TopBarrier = GameMain.World.CreateEdge( 
                ConvertUnits.ToSimUnits(new Vector2(borders.X, 0)), 
                ConvertUnits.ToSimUnits(new Vector2(borders.Right, 0)));

            TopBarrier.SetTransform(ConvertUnits.ToSimUnits(new Vector2(0.0f, borders.Height)), 0.0f);                
            TopBarrier.BodyType = BodyType.Static;
            TopBarrier.CollisionCategories = Physics.CollisionLevel;

            bodies.Add(TopBarrier);

            GenerateSeaFloor();

            if (mirror)
            {
                Point temp = startPosition;
                startPosition = endPosition;
                endPosition = temp;
            }
            if (StartOutpost != null)
            {
                startPosition = new Point((int)StartOutpost.WorldPosition.X, (int)StartOutpost.WorldPosition.Y);
            }
            if (EndOutpost != null)
            {
                endPosition = new Point((int)EndOutpost.WorldPosition.X, (int)EndOutpost.WorldPosition.Y);
            }

            CreateWrecks();
            CreateBeaconStation(cells);

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            LevelObjectManager.PlaceObjects(this, GenerationParams.LevelObjectAmount);

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            GenerateItems();

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

#if CLIENT
            backgroundCreatureManager.SpawnCreatures(this, GenerationParams.BackgroundCreatureAmount);
#endif

            foreach (VoronoiCell cell in cells)
            {
                foreach (GraphEdge edge in cell.Edges)
                {
                    //edge.Cell1 = null;
                    //edge.Cell2 = null;
                    edge.Site1 = null;
                    edge.Site2 = null;
                }
            }

            //initialize MapEntities that aren't in any sub (e.g. items inside ruins)
            MapEntity.MapLoaded(MapEntity.mapEntityList.FindAll(me => me.Submarine == null), false);

            Debug.WriteLine("Generatelevel: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            Debug.WriteLine("**********************************************************************************");
            Debug.WriteLine("Generated a map with " + siteCoordsX.Count + " sites in " + sw.ElapsedMilliseconds + " ms");
            Debug.WriteLine("Seed: " + Seed);
            Debug.WriteLine("**********************************************************************************");

            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Generated level with the seed " + Seed + " (type: " + GenerationParams.Identifier + ")", Color.White);
            }

            EntityCountAfterGenerate = GetEntities().Count();

#if SERVER
            if (GameMain.Server.EntityEventManager.Events.Count() > 0)
            {
                DebugConsole.NewMessage("WARNING: Entity events have been created during level generation. Events should not be created until the round is fully initialized.");
            }
            GameMain.Server.EntityEventManager.Clear();
#endif

            //assign an ID to make entity events work
            //ID = FindFreeID();
            Generating = false;
        }

        private List<Point> GeneratePathNodes(Point startPosition, Point endPosition, Rectangle pathBorders, Tunnel parentTunnel, float variance)
        {
            List<Point> pathNodes = new List<Point> { startPosition };

            Point nodeInterval = GenerationParams.MainPathNodeIntervalRange;

            for (int x = startPosition.X + nodeInterval.X;
                        x < endPosition.X - nodeInterval.X;
                        x += Rand.Range(nodeInterval.X, nodeInterval.Y, Rand.RandSync.Server))
            {
                Point nodePos = new Point(x, Rand.Range(pathBorders.Y, pathBorders.Bottom, Rand.RandSync.Server));

                //allow placing the 2nd main path node at any height regardless of variance
                //(otherwise low variance will always make the main path go through the upper part of the level)
                if (pathNodes.Count > 2 || parentTunnel != null)
                {
                    nodePos.Y = (int)MathHelper.Clamp(
                        nodePos.Y,
                        pathNodes.Last().Y - pathBorders.Height * variance * 0.5f,
                        pathNodes.Last().Y + pathBorders.Height * variance * 0.5f);
                }
                if (pathNodes.Count == 1)
                {
                    //if the path starts below the center of the level, head up and vice versa
                    //to utilize as much of the vertical space as possible
                    nodePos.Y = (int)(startPosition.Y + Math.Abs(nodePos.Y - startPosition.Y) * -Math.Sign(nodePos.Y - pathBorders.Center.Y));
                    nodePos.Y = MathHelper.Clamp(nodePos.Y, pathBorders.Y, pathBorders.Bottom);
                }

                //prevent intersections with other tunnels
                foreach (Tunnel tunnel in Tunnels)
                {
                    for (int i = 1; i < tunnel.Nodes.Count; i++)
                    {
                        Point node1 = tunnel.Nodes[i - 1];
                        Point node2 = tunnel.Nodes[i];
                        if (node1.X >= nodePos.X) { continue; }
                        if (node2.X <= pathNodes.Last().X) { continue; }
                        if (MathUtils.NearlyEqual(node1.X, pathNodes.Last().X)) { continue; }
                        if (Math.Abs(node1.Y - nodePos.Y) > tunnel.MinWidth && Math.Abs(node2.Y - nodePos.Y) > tunnel.MinWidth &&
                            !MathUtils.LinesIntersect(node1.ToVector2(), node2.ToVector2(), pathNodes.Last().ToVector2(), nodePos.ToVector2())) 
                        { 
                            continue; 
                        }

                        if (nodePos.Y < pathNodes.Last().Y)
                        {
                            nodePos.Y = Math.Min(Math.Max(node1.Y, node2.Y) + tunnel.MinWidth * 2, pathBorders.Bottom);
                        }
                        else
                        {
                            nodePos.Y = Math.Max(Math.Min(node1.Y, node2.Y) - tunnel.MinWidth * 2, pathBorders.Y);
                        }
                        break;
                    }
                }

                pathNodes.Add(nodePos);
            }

            if (pathNodes.Count == 1)
            {
                pathNodes.Add(new Point(pathBorders.Center.X, pathBorders.Y));
            }

            pathNodes.Add(endPosition);
            return pathNodes;
        }

        private List<VoronoiCell> CreateHoles(float holeProbability, Rectangle limits, int submarineSize)
        {
            List<VoronoiCell> toBeRemoved = new List<VoronoiCell>();
            foreach (VoronoiCell cell in cells)
            {
                if (GenerationParams.CreateHoleNextToEnd)
                {
                    if ((!Mirrored && cell.Center.X > endPosition.X) || (Mirrored && cell.Center.X < StartPosition.X))
                    {
                        if (cell.Edges.Any(e => e.Point1.Y > Size.Y - submarineSize || e.Point2.Y > Size.Y - submarineSize))
                        {
                            toBeRemoved.Add(cell);
                            continue;
                        }
                    }
                }

                if (cell.Edges.Any(e => e.NextToCave)) { continue; }
                if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) > holeProbability) { continue; }
                if (!limits.Contains(cell.Site.Coord.X, cell.Site.Coord.Y)) { continue; }

                float closestDist = 0.0f;
                Point? closestTunnelNode = null;
                foreach (Tunnel tunnel in Tunnels)
                {
                    foreach (Point node in tunnel.Nodes)
                    {
                        float dist = Math.Abs(cell.Center.X - node.X);
                        if (closestTunnelNode == null || dist < closestDist)
                        {
                            closestDist = dist;
                            closestTunnelNode = node;
                        }
                    } 
                }

                if (closestTunnelNode != null && closestTunnelNode.Value.Y < cell.Center.Y) { continue; }

                toBeRemoved.Add(cell);
            }

            return toBeRemoved;
        }

        private void EnlargePath(List<VoronoiCell> pathCells, float minWidth)
        {
            if (minWidth <= 0.0f) { return; }
            
            List<VoronoiCell> removedCells = GetTooCloseCells(pathCells, minWidth);
            foreach (VoronoiCell removedCell in removedCells)
            {
                if (removedCell.CellType == CellType.Path) { continue; }

                pathCells.Add(removedCell);
                removedCell.CellType = CellType.Path;
            }            
        }

        private void GenerateWaypoints(Tunnel tunnel, Tunnel parentTunnel)
        {
            if (tunnel.Cells.Count == 0) { return; }

            List<WayPoint> wayPoints = new List<WayPoint>();
            for (int i = 0; i < tunnel.Cells.Count; i++)
            {
                tunnel.Cells[i].CellType = CellType.Path;

                var newWaypoint = new WayPoint(new Rectangle((int)tunnel.Cells[i].Site.Coord.X, (int)tunnel.Cells[i].Center.Y, 10, 10), null)
                {
                    Tunnel = tunnel
                };
                wayPoints.Add(newWaypoint);

                if (wayPoints.Count > 1)
                {
                    wayPoints[wayPoints.Count - 2].linkedTo.Add(newWaypoint);
                    newWaypoint.linkedTo.Add(wayPoints[wayPoints.Count - 2]);
                }

                for (int n = 0; n < wayPoints.Count; n++)
                {
                    if (wayPoints[n].Position != newWaypoint.Position) { continue; }

                    wayPoints[n].linkedTo.Add(newWaypoint);
                    newWaypoint.linkedTo.Add(wayPoints[n]);

                    break;
                }
            }

            tunnel.WayPoints.AddRange(wayPoints);

            //connect to the tunnel we're branching off from
            if (parentTunnel != null)
            {
                var parentStart = FindClosestWayPoint(wayPoints.First(), parentTunnel);
                if (parentStart != null)
                {
                    wayPoints.First().linkedTo.Add(parentStart);
                    parentStart.linkedTo.Add(wayPoints.First());
                }
                if (tunnel.Type != TunnelType.Cave || tunnel.ParentTunnel.Type == TunnelType.Cave)
                {
                    var parentEnd = FindClosestWayPoint(wayPoints.Last(), parentTunnel);
                    if (parentEnd != null)
                    {
                        wayPoints.Last().linkedTo.Add(parentEnd);
                        parentEnd.linkedTo.Add(wayPoints.Last());
                    }
                }
            }
        }

        private void ConnectWaypoints(Tunnel tunnel, Tunnel parentTunnel)
        {
            foreach (WayPoint wayPoint in tunnel.WayPoints)
            {
                var closestWaypoint = FindClosestWayPoint(wayPoint, parentTunnel);
                if (closestWaypoint == null) { continue; }
                if (Submarine.PickBody(
                    ConvertUnits.ToSimUnits(wayPoint.WorldPosition),
                    ConvertUnits.ToSimUnits(closestWaypoint.WorldPosition), collisionCategory: Physics.CollisionLevel | Physics.CollisionWall) == null)
                {
                    wayPoint.linkedTo.Add(closestWaypoint);
                    closestWaypoint.linkedTo.Add(wayPoint);
                }
            }
        }

        private static WayPoint FindClosestWayPoint(WayPoint wayPoint, Tunnel otherTunnel)
        {
            float closestDist = float.PositiveInfinity;
            WayPoint closestWayPoint = null;
            foreach (WayPoint otherWayPoint in otherTunnel.WayPoints)
            {
                float dist = Vector2.DistanceSquared(otherWayPoint.WorldPosition, wayPoint.WorldPosition);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestWayPoint = otherWayPoint;

                }
            }
            return closestWayPoint;
        }

        private List<VoronoiCell> GetTooCloseCells(List<VoronoiCell> emptyCells, float minDistance)
        {
            List<VoronoiCell> tooCloseCells = new List<VoronoiCell>();
            if (minDistance <= 0.0f) { return tooCloseCells; }
            foreach (var cell in emptyCells.Distinct())
            {
                foreach (var tooCloseCell in GetTooCloseCells(cell.Center, minDistance))
                {
                    if (!tooCloseCells.Contains(tooCloseCell))
                    {
                        tooCloseCells.Add(tooCloseCell);
                    }
                }
            }
            return tooCloseCells;
        }

        public List<VoronoiCell> GetTooCloseCells(Vector2 position, float minDistance)
        {
            HashSet<VoronoiCell> tooCloseCells = new HashSet<VoronoiCell>();
            var closeCells = GetCells(position, searchDepth: Math.Max((int)Math.Ceiling(minDistance / GridCellSize), 3));
            float minDistSqr = minDistance * minDistance;
            foreach (VoronoiCell cell in closeCells)
            {
                bool tooClose = false;
                foreach (GraphEdge edge in cell.Edges)
                {                    
                    if (Vector2.DistanceSquared(edge.Point1, position) < minDistSqr ||
                        Vector2.DistanceSquared(edge.Point2, position) < minDistSqr ||
                        MathUtils.LineSegmentToPointDistanceSquared(edge.Point1.ToPoint(), edge.Point2.ToPoint(), position.ToPoint()) < minDistSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) { tooCloseCells.Add(cell); }
            }            
            return tooCloseCells.ToList();
        }

        private void GenerateSeaFloorPositions(bool mirror)
        {
            BottomPos = GenerationParams.SeaFloorDepth;
            SeaFloorTopPos = BottomPos;

            bottomPositions = new List<Point>
            {
                new Point(0, BottomPos)
            };

            int mountainCount = Rand.Range(GenerationParams.MountainCountMin, GenerationParams.MountainCountMax, Rand.RandSync.Server);
            for (int i = 0; i < mountainCount; i++)
            {
                bottomPositions.Add(
                    new Point(Size.X / (mountainCount + 1) * (i + 1),
                    BottomPos + Rand.Range(GenerationParams.MountainHeightMin, GenerationParams.MountainHeightMax, Rand.RandSync.Server)));
            }
            bottomPositions.Add(new Point(Size.X, BottomPos));

            int minVertexInterval = 5000;
            float currInverval = Size.X / 2;
            while (currInverval > minVertexInterval)
            {
                for (int i = 0; i < bottomPositions.Count - 1; i++)
                {
                    bottomPositions.Insert(i + 1,
                        new Point(
                            (bottomPositions[i].X + bottomPositions[i + 1].X) / 2,
                            (bottomPositions[i].Y + bottomPositions[i + 1].Y) / 2 + Rand.Range(0, GenerationParams.SeaFloorVariance, Rand.RandSync.Server)));
                    i++;
                }

                currInverval /= 2;
            }

            if (mirror)
            {
                for (int i = 0; i < bottomPositions.Count; i++)
                {
                    bottomPositions[i] = new Point(borders.Size.X - bottomPositions[i].X, bottomPositions[i].Y);
                }
            }

            SeaFloorTopPos = bottomPositions.Max(p => p.Y);
        }

        private void GenerateSeaFloor()
        {            
            SeaFloor = new LevelWall(bottomPositions.Select(p => p.ToVector2()).ToList(), new Vector2(0.0f, -2000.0f), GenerationParams.WallColor, this);
            ExtraWalls.Add(SeaFloor);

            BottomBarrier = GameMain.World.CreateEdge(
                ConvertUnits.ToSimUnits(new Vector2(borders.X, 0)),
                ConvertUnits.ToSimUnits(new Vector2(borders.Right, 0)));

            BottomBarrier.SetTransform(ConvertUnits.ToSimUnits(new Vector2(0.0f, BottomPos)), 0.0f);
            BottomBarrier.BodyType = BodyType.Static;
            BottomBarrier.CollisionCategories = Physics.CollisionLevel;

            bodies.Add(BottomBarrier);
        }

        private void GenerateCaves(Tunnel parentTunnel)
        {
            for (int i = 0; i < GenerationParams.CaveCount; i++)
            {
                var caveParams = CaveGenerationParams.GetRandom(GenerationParams, Rand.RandSync.Server);
                Point caveSize = new Point(
                    Rand.Range(caveParams.MinWidth, caveParams.MaxWidth, Rand.RandSync.Server),
                    Rand.Range(caveParams.MinHeight, caveParams.MaxHeight, Rand.RandSync.Server));
                int radius = Math.Max(caveSize.X, caveSize.Y) / 2;
                int padding = (int)(caveSize.X * 1.2f);
                Rectangle allowedArea = new Rectangle(padding, padding, Size.X - padding * 2, Size.Y - padding * 2);

                var cavePos = FindPosAwayFromMainPath((parentTunnel.MinWidth + radius) * 1.2f, asCloseAsPossible: true, allowedArea);

                Point closestParentNode = parentTunnel.Nodes.First();
                double closestDist = double.PositiveInfinity;
                foreach (Point node in parentTunnel.Nodes)
                {
                    double dist = MathUtils.DistanceSquared((double)node.X, (double)node.Y, (double)cavePos.X, (double)cavePos.Y);
                    if (dist < closestDist)
                    {
                        closestParentNode = node;
                        closestDist = dist;
                    }
                }

                Rectangle caveArea = new Rectangle(cavePos - new Point(caveSize.X / 2, caveSize.Y / 2), caveSize);
                MathUtils.GetLineRectangleIntersection(closestParentNode.ToVector2(), cavePos.ToVector2(), new Rectangle(caveArea.X, caveArea.Y + caveArea.Height, caveArea.Width, caveArea.Height), out Vector2 caveStartPosVector);

                Point caveStartPos = caveStartPosVector.ToPoint();
                Point caveEndPos = cavePos - (caveStartPos - cavePos);

                Cave cave = new Cave(caveParams, caveArea, caveStartPos, caveEndPos);
                Caves.Add(cave);

                var caveSegments = MathUtils.GenerateJaggedLine(
                    caveStartPos.ToVector2(), caveEndPos.ToVector2(), 
                    iterations: 3, 
                    offsetAmount: Vector2.Distance(caveStartPos.ToVector2(), caveEndPos.ToVector2()) * 0.75f);
                if (!caveSegments.Any()) { continue; }

                List<Tunnel> caveBranches = new List<Tunnel>();

                var tunnel = new Tunnel(TunnelType.Cave, SegmentsToNodes(caveSegments), 100, parentTunnel);
                Tunnels.Add(tunnel);
                caveBranches.Add(tunnel);

                int branches = Rand.Range(caveParams.MinBranchCount, caveParams.MaxBranchCount, Rand.RandSync.Server);
                for (int j = 0; j < branches; j++)
                {
                    Tunnel parentBranch = caveBranches.GetRandom(Rand.RandSync.Server);
                    Vector2 branchStartPos = parentBranch.Nodes[Rand.Int(parentBranch.Nodes.Count / 2, Rand.RandSync.Server)].ToVector2();
                    Vector2 branchEndPos = parentBranch.Nodes[Rand.Range(parentBranch.Nodes.Count / 2, parentBranch.Nodes.Count, Rand.RandSync.Server)].ToVector2();
                    var branchSegments = MathUtils.GenerateJaggedLine(
                        branchStartPos, branchEndPos,
                        iterations: 3,
                        offsetAmount: Vector2.Distance(branchStartPos, branchEndPos) * 0.75f);
                    if (!branchSegments.Any()) { continue; }

                    var branch = new Tunnel(TunnelType.Cave, SegmentsToNodes(branchSegments), 0, parentBranch);
                    Tunnels.Add(branch);
                    caveBranches.Add(branch);
                }

                foreach (Tunnel branch in caveBranches)
                {
                    PositionsOfInterest.Add(new InterestingPosition(branch.Nodes.Last(), PositionType.Cave, cave));                    
                    cave.Tunnels.Add(branch);
                }

                static List<Point> SegmentsToNodes(List<Vector2[]> segments)
                {
                    List<Point> nodes = new List<Point>();
                    foreach (Vector2[] segment in segments)
                    {
                        nodes.Add(segment[0].ToPoint());
                    }
                    nodes.Add(segments.Last()[1].ToPoint());
                    return nodes;
                }

                CalculateTunnelDistanceField(density: 1000);
            }
        }

        private void GenerateRuin(Tunnel mainPath, bool mirror)
        {
            var ruinGenerationParams = RuinGenerationParams.GetRandom();

            Point ruinSize = new Point(
                Rand.Range(ruinGenerationParams.SizeMin.X, ruinGenerationParams.SizeMax.X, Rand.RandSync.Server), 
                Rand.Range(ruinGenerationParams.SizeMin.Y, ruinGenerationParams.SizeMax.Y, Rand.RandSync.Server));
            int ruinRadius = Math.Max(ruinSize.X, ruinSize.Y) / 2;

            Point ruinPos = FindPosAwayFromMainPath((ruinRadius + mainPath.MinWidth) * 1.2f, asCloseAsPossible: true, 
                limits: new Rectangle(new Point(ruinSize.X / 2, ruinSize.Y / 2), Size - ruinSize));

            VoronoiCell closestPathCell = null;
            double closestDist = 0.0f;
            foreach (VoronoiCell pathCell in mainPath.Cells)
            {
                double dist = MathUtils.DistanceSquared(pathCell.Site.Coord.X, pathCell.Site.Coord.Y, ruinPos.X, ruinPos.Y);
                if (closestPathCell == null || dist < closestDist)
                {
                    closestPathCell = pathCell;
                    closestDist = dist;
                }
            }
            
            var ruin = new Ruin(closestPathCell, cells, ruinGenerationParams, new Rectangle(ruinPos - new Point(ruinSize.X / 2, ruinSize.Y / 2), ruinSize), mirror);
            Ruins.Add(ruin);
            
            ruin.RuinShapes.Sort((shape1, shape2) => shape2.DistanceFromEntrance.CompareTo(shape1.DistanceFromEntrance));
            // TODO: autogenerate waypoints inside the ruins and connect them to the main path in multiple places.
            // We need the waypoints for the AI navigation and we could use them for spawning the creatures too.
            int waypointCount = 0;
            foreach (WayPoint wp in WayPoint.WayPointList)
            {
                if (wp.SpawnType != SpawnType.Enemy || wp.Submarine != null) { continue; }
                if (ruin.RuinShapes.Any(rs => rs.Rect.Contains(wp.WorldPosition)))
                {
                    PositionsOfInterest.Add(new InterestingPosition(new Point((int)wp.WorldPosition.X, (int)wp.WorldPosition.Y), PositionType.Ruin, ruin: ruin));
                    waypointCount++;
                }
            }

            //not enough waypoints inside ruins -> create some spawn positions manually            
            for (int i = 0; i < 4 - waypointCount && i < ruin.RuinShapes.Count; i++)
            {
                PositionsOfInterest.Add(new InterestingPosition(ruin.RuinShapes[i].Rect.Center, PositionType.Ruin, ruin: ruin));
            }

            foreach (RuinShape ruinShape in ruin.RuinShapes)
            {
                var tooClose = GetTooCloseCells(ruinShape.Rect.Center.ToVector2(), Math.Max(ruinShape.Rect.Width, ruinShape.Rect.Height));

                foreach (VoronoiCell cell in tooClose)
                {
                    if (cell.CellType == CellType.Empty) { continue; }
                    if (ExtraWalls.Any(w => w.Cells.Contains(cell))) { continue; }
                    foreach (GraphEdge e in cell.Edges)
                    {
                        Rectangle rect = ruinShape.Rect;
                        rect.Y += rect.Height;
                        if (ruinShape.Rect.Contains(e.Point1) || ruinShape.Rect.Contains(e.Point2) ||
                            MathUtils.GetLineRectangleIntersection(e.Point1, e.Point2, rect, out _))
                        {
                            cell.CellType = CellType.Removed;
                            for (int x = 0; x < cellGrid.GetLength(0); x++)
                            {
                                for (int y = 0; y < cellGrid.GetLength(1); y++)
                                {
                                    cellGrid[x, y].Remove(cell);
                                }
                            }
                            cells.Remove(cell);
                            break;
                        }
                    }
                }
            }

            CreatePathToClosestTunnel(ruinPos);
        }

        private Point FindPosAwayFromMainPath(double minDistance, bool asCloseAsPossible, Rectangle? limits = null)
        {
            var validPoints = distanceField.FindAll(d => d.Second >= minDistance && (limits == null || limits.Value.Contains(d.First)));
            validPoints.RemoveAll(d => d.First.Y < GetBottomPosition(d.First.X).Y + minDistance);
            if (asCloseAsPossible || !validPoints.Any())
            {
                if (!validPoints.Any()) { validPoints = distanceField; }
                Pair<Point, double> closestPoint = null;
                foreach (var point in validPoints)
                {
                    if (closestPoint == null || point.Second < closestPoint.Second)
                    {
                        closestPoint = point;
                    }
                }
                return closestPoint.First;
            }
            else
            {
                return validPoints[Rand.Int(validPoints.Count, Rand.RandSync.Server)].First;
            }
        }

        private void CalculateTunnelDistanceField(int density)
        {
            distanceField = new List<Pair<Point, double>>();
            for (int x = 0; x < Size.X; x += density)
            {
                for (int y = 0; y < Size.Y; y += density)
                {
                    Point point = new Point(x, y);
                    double shortestDistSqr = double.PositiveInfinity;
                    foreach (Tunnel tunnel in Tunnels)
                    {
                        for (int i = 1; i < tunnel.Nodes.Count; i++)
                        {
                            shortestDistSqr = Math.Min(shortestDistSqr, MathUtils.LineSegmentToPointDistanceSquared(tunnel.Nodes[i - 1], tunnel.Nodes[i], point));
                        }
                    }
                    shortestDistSqr = Math.Min(shortestDistSqr, MathUtils.DistanceSquared((double)point.X, (double)point.Y, (double)startPosition.X, (double)startPosition.Y));
                    shortestDistSqr = Math.Min(shortestDistSqr, MathUtils.DistanceSquared((double)point.X, (double)point.Y, (double)endPosition.X, (double)endPosition.Y));
                    distanceField.Add(new Pair<Point, double>(point, Math.Sqrt(shortestDistSqr)));
                }
            }
        }

        private double GetDistToTunnel(Vector2 position, Tunnel tunnel)
        {
            Point point = position.ToPoint();
            double shortestDistSqr = double.PositiveInfinity;
            for (int i = 1; i < tunnel.Nodes.Count; i++)
            {
                shortestDistSqr = Math.Min(shortestDistSqr, MathUtils.LineSegmentToPointDistanceSquared(tunnel.Nodes[i - 1], tunnel.Nodes[i], point));
            }
            return Math.Sqrt(shortestDistSqr);
        }

        private DestructibleLevelWall CreateIceChunk(IEnumerable<GraphEdge> edges, Vector2 position, float? health = null)
        {
            List<Vector2> vertices = new List<Vector2>();
            foreach (GraphEdge edge in edges)
            {
                if (!vertices.Any())
                {
                    vertices.Add(edge.Point1);
                }
                else if (!vertices.Any(v => v.NearlyEquals(edge.Point1)))
                {
                    vertices.Add(edge.Point1);
                }
                else if (!vertices.Any(v => v.NearlyEquals(edge.Point2)))
                {
                    vertices.Add(edge.Point2);
                }
            }
            if (vertices.Count < 3) { return null; }
            return CreateIceChunk(vertices.Select(v => v - position).ToList(), position, health);
        }

        private DestructibleLevelWall CreateIceChunk(List<Vector2> vertices, Vector2 position, float? health = null)
        {
            DestructibleLevelWall newChunk = new DestructibleLevelWall(vertices, Color.White, this, health, true);
            newChunk.Body.Position = ConvertUnits.ToSimUnits(position);
            newChunk.Cells.ForEach(c => c.Translation = position);
            newChunk.Body.BodyType = BodyType.Dynamic;
            newChunk.Body.FixedRotation = true;
            newChunk.Body.LinearDamping = 0.5f;
            newChunk.Body.IgnoreGravity = true;
            newChunk.Body.Mass *= 10.0f;
            return newChunk;
        }

        private DestructibleLevelWall CreateIceSpire(List<GraphEdge> usedSpireEdges)
        {
            const float maxLength = 15000.0f;

            var mainPathPos = PositionsOfInterest.Where(pos => pos.PositionType == PositionType.MainPath).GetRandom(Rand.RandSync.Server);
            double closestDistSqr = double.PositiveInfinity;
            GraphEdge closestEdge = null;
            VoronoiCell closestCell = null;
            foreach (VoronoiCell cell in cells)
            {
                if (cell.CellType != CellType.Solid) { continue; }
                foreach (GraphEdge edge in cell.Edges)
                {
                    if (!edge.IsSolid || usedSpireEdges.Contains(edge) || edge.NextToCave) { continue; }
                    //don't spawn spires near the start/end of the level
                    if (edge.Center.Y > Size.Y / 2 && (edge.Center.X < Size.X * 0.3f || edge.Center.X > Size.X * 0.7f)) { continue; }
                    if (Vector2.DistanceSquared(edge.Center, StartPosition) < maxLength * maxLength) { continue; }
                    if (Vector2.DistanceSquared(edge.Center, EndPosition) < maxLength * maxLength) { continue; }
                    //don't spawn on very long edges
                    if (Vector2.DistanceSquared(edge.Point1, edge.Point2) > 1000.0f * 1000.0f) { continue; }
                    //don't spawn on edges facing away from the main path
                    if (Vector2.Dot(Vector2.Normalize(mainPathPos.Position.ToVector2()) - edge.Center, edge.GetNormal(cell)) < 0.5f) { continue; }
                    double distSqr = MathUtils.DistanceSquared(edge.Center.X, edge.Center.Y, mainPathPos.Position.X, mainPathPos.Position.Y);
                    if (distSqr < closestDistSqr)
                    {
                        closestDistSqr = distSqr;
                        closestEdge = edge; 
                        closestCell = cell;
                    }
                }
            }

            if (closestEdge == null) { return null; }

            usedSpireEdges.Add(closestEdge);

            Vector2 edgeNormal = closestEdge.GetNormal(closestCell);
            float spireLength = (float)Math.Min(Math.Sqrt(closestDistSqr), maxLength);
            spireLength *= MathHelper.Lerp(0.3f, 1.5f, Difficulty / 100.0f);

            Vector2 extrudedPoint1 = closestEdge.Point1 + edgeNormal * spireLength * Rand.Range(0.8f, 1.0f, Rand.RandSync.Server);
            Vector2 extrudedPoint2 = closestEdge.Point2 + edgeNormal * spireLength * Rand.Range(0.8f, 1.0f, Rand.RandSync.Server);
            List<Vector2> vertices = new List<Vector2>()
            {
                closestEdge.Point1,
                extrudedPoint1 + (extrudedPoint2 - extrudedPoint1) * Rand.Range(0.3f, 0.45f, Rand.RandSync.Server),
                extrudedPoint2 + (extrudedPoint1 - extrudedPoint2) * Rand.Range(0.3f, 0.45f, Rand.RandSync.Server),
                closestEdge.Point2,
            };
            Vector2 center = Vector2.Zero;
            vertices.ForEach(v => center += v);
            center /= vertices.Count;
            DestructibleLevelWall spire = new DestructibleLevelWall(vertices.Select(v => v - center).ToList(), Color.White, this, health: 100.0f, giftWrap: true);
#if CLIENT
            //make the edge at the bottom of the spire non-solid
            foreach (GraphEdge edge in spire.Cells[0].Edges)
            {
                if ((edge.Point1.NearlyEquals(closestEdge.Point1 - center) && edge.Point2.NearlyEquals(closestEdge.Point2 - center)) ||
                    (edge.Point1.NearlyEquals(closestEdge.Point2 - center) && edge.Point2.NearlyEquals(closestEdge.Point1 - center)))
                {
                    edge.IsSolid = false;
                    break;
                }
            }
            spire.GenerateVertices();
#endif
            spire.Body.Position = ConvertUnits.ToSimUnits(center);
            spire.Body.BodyType = BodyType.Static;
            spire.Body.FixedRotation = true;
            spire.Body.IgnoreGravity = true;
            spire.Body.Mass *= 10.0f;
            spire.Cells.ForEach(c => c.Translation = center);
            spire.WallDamageOnTouch = 50.0f;
            return spire;
        }

        // TODO: Improve this temporary level editor debug solution (or remove it)
        private static int nextPathPointId;
        public List<PathPoint> PathPoints { get; } = new List<PathPoint>();
        public struct PathPoint
        {
            public string Id { get; }
            public Vector2 Position { get; }
            public bool ShouldContainResources { get; set; }
            public float NextClusterProbability
            {
                get
                {
                    return ClusterLocations.Count switch
                    {
                        1 => 5.0f,
                        2 => 2.5f,
                        3 => 1.0f,
                        _ => 0.0f,
                    };
                }
            }
            public List<string> ResourceTags { get; }
            public List<string> ResourceIds { get; }
            public List<ClusterLocation> ClusterLocations { get; }
            public TunnelType TunnelType { get; }

            public PathPoint(string id, Vector2 position, bool shouldContainResources, TunnelType tunnelType)
            {
                Id = id;   
                Position = position;
                ShouldContainResources = shouldContainResources;
                ResourceTags = new List<string>();
                ResourceIds = new List<string>();
                ClusterLocations = new List<ClusterLocation>();
                TunnelType = tunnelType;
            }
        }

        public struct ClusterLocation
        {
            public VoronoiCell Cell { get; }
            public GraphEdge Edge { get; }
            public Vector2 EdgeCenter { get; }
            /// <summary>
            /// Can be null unless initialized in constructor
            /// </summary>
            public List<Item> Resources { get; private set; }

            /// <param name="initializeResourceList">List is initialized only when specified, otherwise will be null</param>
            public ClusterLocation(VoronoiCell cell, GraphEdge edge, bool initializeResourceList = false)
            {
                Cell = cell;
                Edge = edge;
                EdgeCenter = edge.Center;
                Resources = initializeResourceList ? new List<Item>() : null;
            }

            public bool Equals(ClusterLocation anotherLocation) =>
                Cell == anotherLocation.Cell && Edge == anotherLocation.Edge;

            public bool Equals(VoronoiCell cell, GraphEdge edge) =>
                Cell == cell && Edge == edge;

            public void InitializeResources()
            {
                Resources = new List<Item>();
            }
        }

        // TODO: Take into account items which aren't ores or plants
        // Such as the exploding crystals in The Great Sea
        private void GenerateItems()
        {
            string levelName = GenerationParams.Identifier.ToLowerInvariant();
            float minCommonness = float.MaxValue, maxCommonness = float.MinValue;
            List<Pair<ItemPrefab, float>> levelResources = new List<Pair<ItemPrefab, float>>();
            var fixedResources = new List<Tuple<ItemPrefab, ItemPrefab.FixedQuantityResourceInfo>>();
            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                if (itemPrefab.LevelCommonness.TryGetValue(levelName, out float commonness) || 
                    itemPrefab.LevelCommonness.TryGetValue("", out commonness))
                {
                    if (commonness <= 0.0f) { continue; }
                    if (commonness < minCommonness) { minCommonness = commonness; }
                    if (commonness > maxCommonness) { maxCommonness = commonness; }
                    levelResources.Add(new Pair<ItemPrefab, float>(itemPrefab, commonness));
                }
                else if (itemPrefab.LevelQuantity.TryGetValue(levelName, out var fixedQuantityResourceInfo) ||
                         itemPrefab.LevelQuantity.TryGetValue("", out fixedQuantityResourceInfo))
                {
                    fixedResources.Add(new Tuple<ItemPrefab, ItemPrefab.FixedQuantityResourceInfo>(itemPrefab, fixedQuantityResourceInfo));
                }
            }

            DebugConsole.Log("Generating level resources...");
            var allValidLocations = GetAllValidClusterLocations();
            var maxResourceOverlap = 0.4f;

            foreach (var fixedResource in fixedResources)
            {
                for (int i = 0; i < fixedResource.Item2.ClusterQuantity; i++)
                {
                    var location = allValidLocations.GetRandom(l =>
                    {
                        if (l.Cell == null || l.Edge == null) { return false; }
                        if (fixedResource.Item2.IsIslandSpecifc && !l.Cell.Island) { return false; }
                        return fixedResource.Item2.ClusterSize <= GetMaxResourcesOnEdge(fixedResource.Item1, l, out _);

                    }, randSync: Rand.RandSync.Server);

                    if (location.Cell == null || location.Edge == null) { break; }

                    PlaceResources(fixedResource.Item1, fixedResource.Item2.ClusterSize, location, out _);
                    var locationIndex = allValidLocations.FindIndex(l => l.Equals(location));
                    allValidLocations.RemoveAt(locationIndex);
                }
            }

            PathPoints.Clear();
            nextPathPointId = 0;
            
            foreach (Tunnel tunnel in Tunnels)
            {
                var tunnelLength = 0.0f;
                for (int i = 1; i < tunnel.Nodes.Count; i++)
                {
                    tunnelLength += Vector2.Distance(tunnel.Nodes[i - 1].ToVector2(), tunnel.Nodes[i].ToVector2());
                }

                var nextNodeIndex = 1;
                var positionOnPath = tunnel.Nodes.First().ToVector2();
                var lastNodePos = tunnel.Nodes.Last().ToVector2();
                var reachedLastNode = false;
                var intervalRange = tunnel.Type != TunnelType.Cave ? GenerationParams.ResourceIntervalRange : GenerationParams.CaveResourceIntervalRange;
                do
                {
                    var distance = Rand.Range(intervalRange.X, intervalRange.Y, sync: Rand.RandSync.Server);
                    reachedLastNode = !CalculatePositionOnPath();
                    var id = Tunnels.IndexOf(tunnel) + ":" + nextPathPointId++;
                    var spawnChance = tunnel.Type == TunnelType.Cave || tunnel.ParentTunnel?.Type == TunnelType.Cave ?
                        GenerationParams.CaveResourceSpawnChance : GenerationParams.ResourceSpawnChance;
                    var containsResources = true;
                    if (spawnChance < 1.0f)
                    {
                        var spawnPointRoll = Rand.Range(0.0f, 1.0f, sync: Rand.RandSync.Server);
                        containsResources = spawnPointRoll <= spawnChance;
                    }
                    var tunnelType = tunnel.Type;
                    if (tunnel.ParentTunnel != null && tunnel.ParentTunnel.Type == TunnelType.Cave) { tunnelType = TunnelType.Cave;  }
                    PathPoints.Add(new PathPoint(id, positionOnPath, containsResources, tunnel.Type));

                    bool CalculatePositionOnPath(float checkedDist = 0.0f)
                    {
                        if (nextNodeIndex >= tunnel.Nodes.Count) { return false; }
                        var distToNextNode = Vector2.Distance(positionOnPath, tunnel.Nodes[nextNodeIndex].ToVector2());
                        var lerpAmount = (distance - checkedDist) / distToNextNode;
                        if (lerpAmount <= 1.0f)
                        {
                            positionOnPath = Vector2.Lerp(positionOnPath, tunnel.Nodes[nextNodeIndex].ToVector2(), lerpAmount);
                            return true;
                        }
                        else
                        {
                            positionOnPath = tunnel.Nodes[nextNodeIndex++].ToVector2();
                            return CalculatePositionOnPath(checkedDist + distToNextNode);
                        }
                    }
                } while (!reachedLastNode && Vector2.DistanceSquared(positionOnPath, lastNodePos) > (intervalRange.Y * intervalRange.Y));
            }

            int itemCount = 0;
            string[] exclusiveResourceTags = new string[2] { "ore", "plant" };

            // Create first cluster for each spawn point
            foreach (var pathPoint in PathPoints.Where(p => p.ShouldContainResources))
            {
                if (itemCount >= GenerationParams.ItemCount) { break; }
                GenerateFirstCluster(pathPoint);
            }

            // Don't try to spawn more resource clusters for points
            // for which the initial cluster could not be spawned
            PathPoints.Where(p => p.ShouldContainResources && p.ClusterLocations.Count == 0)
                .ForEach(p => p.ShouldContainResources = false);

            var excludedPathPointIds = new List<string>();
            while (itemCount < GenerationParams.ItemCount)
            {
                var availablePathPoints = PathPoints.Where(p =>
                    p.ShouldContainResources && p.NextClusterProbability > 0 &&
                    !excludedPathPointIds.Contains(p.Id));

                if (availablePathPoints.None()) { break; }

                var pathPoint = ToolBox.SelectWeightedRandom(
                    availablePathPoints.ToList(),
                    availablePathPoints.Select(p => p.NextClusterProbability).ToList(),
                    Rand.RandSync.Server);

                GenerateAdditionalCluster(pathPoint);
            }

            // If none of the point set to contain resources can take more resources,
            // but we still haven't reached the item count set in the generation parameters...
            while (itemCount < GenerationParams.ItemCount)
            {
                // We need to start filling some of the path points previously set to not contain resources
                var availablePathPoints = PathPoints.Where(p => !excludedPathPointIds.Contains(p.Id) && p.ClusterLocations.None());
                if (availablePathPoints.None()) { break; }
                var pathPoint = availablePathPoints.GetRandom(randSync: Rand.RandSync.Server);
                if (!GenerateFirstCluster(pathPoint))
                {
                    excludedPathPointIds.Add(pathPoint.Id);
                    continue;
                }
                while (pathPoint.NextClusterProbability > 0)
                {
                    if (!GenerateAdditionalCluster(pathPoint)) { break; }
                }
                pathPoint.ShouldContainResources = pathPoint.ClusterLocations.Any();
            }

#if DEBUG
            DebugConsole.NewMessage("Level resources spawned: " + itemCount + "\n" +
                "Spawn points containing resources: " + PathPoints.Where(p => p.ClusterLocations.Any()).Count() + "/" + PathPoints.Count);
#endif

            DebugConsole.Log("Level resources generated");

            bool GenerateFirstCluster(PathPoint pathPoint)
            {
                var intervalRange = pathPoint.TunnelType != TunnelType.Cave ?
                    GenerationParams.ResourceIntervalRange : GenerationParams.CaveResourceIntervalRange;
                allValidLocations.Sort((x, y) =>
                    Vector2.DistanceSquared(pathPoint.Position, x.EdgeCenter)
                    .CompareTo(Vector2.DistanceSquared(pathPoint.Position, y.EdgeCenter)));
                var selectedLocationIndex = -1;
                var generatedCluster = false;
                for (int i = 0; i < allValidLocations.Count; i++)
                {
                    var validLocation = allValidLocations[i];
                    if (!IsNextToTunnelType(validLocation.Edge, pathPoint.TunnelType)) { continue; }
                    var distanceSquaredToEdge = Vector2.DistanceSquared(pathPoint.Position, validLocation.EdgeCenter);
                    // Edge isn't too far from the path point
                    if (distanceSquaredToEdge > 3.0f * (intervalRange.Y * intervalRange.Y)) { continue; }
                    // Edge is closer to the path point than the cell center
                    if (distanceSquaredToEdge > Vector2.DistanceSquared(pathPoint.Position, validLocation.Cell.Center)) { continue; }

                    var validComparedToOtherPathPoints = true;
                    // Make sure this path point is closest to 'validLocation'
                    foreach (var anotherPathPoint in PathPoints)
                    {
                        if (anotherPathPoint.Id == pathPoint.Id) { continue; }
                        if (Vector2.DistanceSquared(anotherPathPoint.Position, validLocation.EdgeCenter) < distanceSquaredToEdge)
                        {
                            validComparedToOtherPathPoints = false;
                            break;
                        }
                    }

                    foreach (var anotherPathPoint in PathPoints.Where(p => p.Id != pathPoint.Id && p.ClusterLocations.Any()))
                    {
                        if (!validComparedToOtherPathPoints) { break; }
                        foreach (var c in pathPoint.ClusterLocations)
                        {
                            if (IsInvalidComparedToExistingLocation())
                            {
                                validComparedToOtherPathPoints = false;
                                break;
                            }

                            bool IsInvalidComparedToExistingLocation()
                            {
                                if (c.Equals(validLocation)) { return true; }
                                // If there is a previously spawned cluster too near
                                if (Vector2.DistanceSquared(c.EdgeCenter, validLocation.EdgeCenter) > (intervalRange.X * intervalRange.X))  { return true; }
                                // If there is a line from a previous path point to one of its existing cluster locations
                                // which intersects with the line from this path point to the new possible cluster location
                                if (MathUtils.LinesIntersect(anotherPathPoint.Position, c.EdgeCenter, pathPoint.Position, validLocation.EdgeCenter))  { return true; }
                                return false;
                            }
                        }
                    }

                    if (!validComparedToOtherPathPoints) { continue; }
                    generatedCluster = CreateResourceCluster(pathPoint, validLocation);
                    selectedLocationIndex = i;
                    break;
                }

                if (selectedLocationIndex >= 0)
                {
                    allValidLocations.RemoveAt(selectedLocationIndex);
                }

                return generatedCluster;

                static bool IsNextToTunnelType(GraphEdge e, TunnelType t) =>
                    (e.NextToMainPath && t == TunnelType.MainPath) ||
                    (e.NextToSidePath && t == TunnelType.SidePath) ||
                    (e.NextToCave && t == TunnelType.Cave);
            }

            bool GenerateAdditionalCluster(PathPoint pathPoint)
            {
                var validLocations = new List<ClusterLocation>();
                // First check only the edges of the same cell
                // which are connected to one of the existing edges with clusters
                foreach (var clusterLocation in pathPoint.ClusterLocations)
                {
                    foreach (var anotherEdge in clusterLocation.Cell.Edges.Where(e => e != clusterLocation.Edge))
                    {
                        if (HaveConnectingEdgePoints(anotherEdge, clusterLocation.Edge))
                        {
                            AddIfValid(clusterLocation.Cell, anotherEdge);
                        }
                    }
                }

                // Only check edges of adjacent cells if no valid edges were found
                // on any of the cells with existing clusters
                if (validLocations.None())
                {
                    foreach (var clusterLocation in pathPoint.ClusterLocations)
                    {
                        foreach (var anotherEdge in clusterLocation.Cell.Edges.Where(e => e != clusterLocation.Edge))
                        {
                            var adjacentCell = anotherEdge.AdjacentCell(clusterLocation.Cell);
                            if (adjacentCell == null) { continue; }
                            foreach (var adjacentCellEdge in adjacentCell.Edges.Where(e => e != anotherEdge))
                            {
                                if (HaveConnectingEdgePoints(adjacentCellEdge, clusterLocation.Edge))
                                {
                                    AddIfValid(adjacentCell, adjacentCellEdge);
                                }
                            }
                        }
                    }
                }

                if (validLocations.Any())
                {
                    var location = validLocations.GetRandom(randSync: Rand.RandSync.Server);
                    if (CreateResourceCluster(pathPoint, location))
                    {
                        var i = allValidLocations.FindIndex(l => l.Equals(location));
                        if (i >= 0)
                        {
                            allValidLocations.RemoveAt(i);
                        }
                        return true;
                    }
                    else
                    {
                        excludedPathPointIds.Add(pathPoint.Id);
                        return false;
                    }
                }
                else
                {
                    excludedPathPointIds.Add(pathPoint.Id);
                    return false;
                }

                static bool HaveConnectingEdgePoints(GraphEdge e1, GraphEdge e2) =>
                    e1.Point1.NearlyEquals(e2.Point1) || e1.Point1.NearlyEquals(e2.Point2) ||
                    e1.Point2.NearlyEquals(e2.Point1) || e1.Point2.NearlyEquals(e2.Point2);

                void AddIfValid(VoronoiCell c, GraphEdge e)
                {
                    if (IsAlreadyInList(e)) { return; }
                    if (allValidLocations.None(l => l.Equals(c, e))) { return; }
                    if (pathPoint.ClusterLocations.Any(cl => cl.Edge == e)) { return; }
                    validLocations.Add(new ClusterLocation(c, e));
                }

                bool IsAlreadyInList(GraphEdge edge) =>
                    validLocations.Any(l => l.Edge == edge);
            }

            bool CreateResourceCluster(PathPoint pathPoint, ClusterLocation location)
            {
                if (location.Cell == null || location.Edge == null) { return false; }

                ItemPrefab selectedPrefab;
                if (pathPoint.ClusterLocations.Count == 0)
                {
                    selectedPrefab = ToolBox.SelectWeightedRandom(
                        levelResources.Select(it => it.First).ToList(),
                        levelResources.Select(it => it.Second).ToList(),
                        Rand.RandSync.Server);
                    selectedPrefab.Tags.ForEach(t =>
                    {
                        if (exclusiveResourceTags.Contains(t))
                        {
                            pathPoint.ResourceTags.Add(t);
                        }
                    });
                }
                else
                {
                    var filteredResources = levelResources.Where(it =>
                        !pathPoint.ResourceIds.Contains(it.First.Identifier) &&
                        pathPoint.ResourceTags.Any() && it.First.Tags.Any(t => pathPoint.ResourceTags.Contains(t)));
                    selectedPrefab = ToolBox.SelectWeightedRandom(
                        filteredResources.Select(it => it.First).ToList(),
                        filteredResources.Select(it => it.Second).ToList(),
                        Rand.RandSync.Server);
                }

                if (selectedPrefab == null) { return false; }

                // Create resources for the cluster
                var commonness = levelResources.First(r => r.First == selectedPrefab).Second;
                var lerpAmount = MathUtils.InverseLerp(minCommonness, maxCommonness, commonness);
                var maxClusterSize = (int)MathHelper.Lerp(GenerationParams.ResourceClusterSizeRange.X, GenerationParams.ResourceClusterSizeRange.Y, lerpAmount);
                var maxFitOnEdge = GetMaxResourcesOnEdge(selectedPrefab, location, out var edgeLength);
                maxClusterSize = Math.Min(maxClusterSize, maxFitOnEdge);
                if (itemCount + maxClusterSize > GenerationParams.ItemCount)
                {
                    maxClusterSize += GenerationParams.ItemCount - (itemCount + maxClusterSize);
                }

                if (maxClusterSize < 1) { return false; }

                var minClusterSize = Math.Min(GenerationParams.ResourceClusterSizeRange.X, maxClusterSize);
                var resourcesInCluster = maxClusterSize == 1 ? 1 : Rand.Range(minClusterSize, maxClusterSize + 1, sync: Rand.RandSync.Server);

                if (resourcesInCluster < 1) { return false; }

                PlaceResources(selectedPrefab, resourcesInCluster, location, out var placedResources, edgeLength: edgeLength);
                itemCount += resourcesInCluster;
                location.InitializeResources();
                location.Resources.AddRange(placedResources);
                pathPoint.ClusterLocations.Add(location);
                pathPoint.ResourceIds.Add(selectedPrefab.Identifier);

                return true;
            }

            int GetMaxResourcesOnEdge(ItemPrefab resourcePrefab, ClusterLocation location, out float edgeLength)
            {
                edgeLength = 0.0f;
                if (location.Cell == null || location.Edge == null) { return 0; } 
                edgeLength = Vector2.Distance(location.Edge.Point1, location.Edge.Point2);
                return (int)Math.Floor(edgeLength / ((1.0f - maxResourceOverlap) * resourcePrefab.Size.X));
            }
        }

        /// <param name="rotation">Used by clients to set the rotation for the resources</param>
        public List<Item> GenerateMissionResources(ItemPrefab prefab, int requiredAmount, out float rotation)
        {
            var allValidLocations = GetAllValidClusterLocations();
            var placedResources = new List<Item>();
            rotation = 0.0f;
            if (allValidLocations.None()) { return placedResources; } // TODO: WHAT?!

            for (int i = allValidLocations.Count - 1; i >= 0; i--)
            {
                var location = allValidLocations[i];
                var locationHasResources = PathPoints.Any(p =>
                    p.ClusterLocations.Any(c =>
                        c.Equals(location) &&
                        c.Resources.Any(r => r != null && !r.Removed &&
                            (!(r.GetComponent<Holdable>() is Holdable h) || (h.Attachable && h.Attached)))));
                if (locationHasResources)
                {
                    allValidLocations.RemoveAt(i);
                }
            }

            var positionType = PositionType.MainPath;
            if (PositionsOfInterest.Any(p => p.PositionType == PositionType.Cave))
            {
                positionType = PositionType.Cave;
            }
            else if (PositionsOfInterest.Any(p => p.PositionType == PositionType.SidePath))
            {
                positionType = PositionType.SidePath;
            }

            var poi = PositionsOfInterest.GetRandom(p => p.PositionType == positionType, randSync: Rand.RandSync.Server);
            var poiPos = poi.Position.ToVector2();
            allValidLocations.Sort((x, y) => Vector2.DistanceSquared(poiPos, x.EdgeCenter)
                .CompareTo(Vector2.DistanceSquared(poiPos, y.EdgeCenter)));
            var maxResourceOverlap = 0.4f;
            // TODO: Find multiple locations if there's too many resources to fit on a sigle edge
            var selectedLocation = allValidLocations.FirstOrDefault(l =>
                Vector2.Distance(l.Edge.Point1, l.Edge.Point2) is float edgeLength &&
                requiredAmount <= (int)Math.Floor(edgeLength / ((1.0f - maxResourceOverlap) * prefab.Size.X)));
            PlaceResources(prefab, requiredAmount, selectedLocation, out placedResources);
            var edgeNormal = selectedLocation.Edge.GetNormal(selectedLocation.Cell);
            rotation = MathHelper.ToDegrees(-MathUtils.VectorToAngle(edgeNormal) + MathHelper.PiOver2);
            return placedResources;
        }

        private List<ClusterLocation> GetAllValidClusterLocations()
        {
            var subBorders = new List<Rectangle>();
            Wrecks.ForEach(w => AddBordersToList(w));
            AddBordersToList(BeaconStation);

            var locations = new List<ClusterLocation>();
            foreach (var c in GetAllCells())
            {
                if (c.CellType != CellType.Solid) { continue; }
                foreach (var e in c.Edges)
                {
                    if (IsValidEdge(e))
                    {
                        locations.Add(new ClusterLocation(c, e));
                    }
                }
            }
            return locations;

            void AddBordersToList(Submarine s)
            {
                if (s == null) { return; }
                var rect = Submarine.AbsRect(s.WorldPosition, s.Borders.Size.ToVector2());
                subBorders.Add(rect);
            }

            bool IsValidEdge(GraphEdge e)
            {
                if (!e.IsSolid) { return false; }
                if (e.OutsideLevel) { return false; }
                var eCenter = e.Center;
                if (IsBlockedByWreckOrBeacon()) { return false; }
                if (IsBlockedByWall()) { return false; }
                return true;

                bool IsBlockedByWreckOrBeacon()
                {
                    foreach (var r in subBorders)
                    {
                        if (Submarine.RectContains(r, e.Point1)) { return true; }
                        if (Submarine.RectContains(r, e.Point2)) { return true; }
                        if (Submarine.RectContains(r, eCenter)) { return true; }
                    }
                    return false;
                }

                bool IsBlockedByWall()
                {
                    foreach (var w in ExtraWalls)
                    {
                        foreach (var c in w.Cells)
                        {
                            if (c.IsPointInside(eCenter)) { return true; }
                            if (c.IsPointInside(eCenter - 100 * e.GetNormal(c))) { return true; }
                            if (c.Edges.Any(extraWallEdge => extraWallEdge == e)) { return true; }
                        }
                    }
                    return false;
                }
            }
        }

        private void PlaceResources(ItemPrefab resourcePrefab, int resourceCount, ClusterLocation location, out List<Item> placedResources,
            float? edgeLength = null, float maxResourceOverlap = 0.4f)
        {
            edgeLength ??= Vector2.Distance(location.Edge.Point1, location.Edge.Point2);
            var minResourceOverlap = -((edgeLength.Value - (resourceCount * resourcePrefab.Size.X)) / (resourceCount * resourcePrefab.Size.X));
            minResourceOverlap = Math.Max(minResourceOverlap, 0.0f);
            var lerpAmounts = new float[resourceCount];
            lerpAmounts[0] = 0.0f;
            var lerpAmount = 0.0f;
            for (int i = 1; i < resourceCount; i++)
            {
                var overlap = Rand.Range(minResourceOverlap, maxResourceOverlap, sync: Rand.RandSync.Server);
                lerpAmount += ((1.0f - overlap) * resourcePrefab.Size.X) / edgeLength.Value;
                lerpAmounts[i] = Math.Clamp(lerpAmount, 0.0f, 1.0f);
            }
            var startOffset = Rand.Range(0.0f, 1.0f - lerpAmount, sync: Rand.RandSync.Server);
            placedResources = new List<Item>();
            for (int i = 0; i < resourceCount; i++)
            {
                Vector2 selectedPos = Vector2.Lerp(location.Edge.Point1, location.Edge.Point2, startOffset + lerpAmounts[i]);
                var item = new Item(resourcePrefab, selectedPos, submarine: null);
                Vector2 edgeNormal = location.Edge.GetNormal(location.Cell);
                float moveAmount = (item.body == null ? item.Rect.Height / 2 : ConvertUnits.ToDisplayUnits(item.body.GetMaxExtent() * 0.7f));
                moveAmount += (item.GetComponent<LevelResource>()?.RandomOffsetFromWall ?? 0.0f) * Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server);
                item.Move(edgeNormal * moveAmount, ignoreContacts: true);
                if (item.GetComponent<Holdable>() is Holdable h)
                {
                    h.AttachToWall();
#if CLIENT
                    item.Rotation = MathHelper.ToDegrees(-MathUtils.VectorToAngle(edgeNormal) + MathHelper.PiOver2);
#endif
                }
                else if (item.body != null)
                {
                    item.body.SetTransformIgnoreContacts(item.body.SimPosition, MathUtils.VectorToAngle(edgeNormal) - MathHelper.PiOver2);
                }
                placedResources.Add(item);
            }
        }

        public Vector2 GetRandomItemPos(PositionType spawnPosType, float randomSpread, float minDistFromSubs, float offsetFromWall = 10.0f, Func<InterestingPosition, bool> filter = null)
        {
            if (!PositionsOfInterest.Any())
            {
                return new Vector2(Size.X / 2, Size.Y / 2);
            }

            Vector2 position = Vector2.Zero;

            int tries = 0;
            do
            {
                TryGetInterestingPosition(true, spawnPosType, minDistFromSubs, out Vector2 startPos, filter);

                Vector2 offset = Rand.Vector(Rand.Range(0.0f, randomSpread, Rand.RandSync.Server), Rand.RandSync.Server);
                if (!cells.Any(c => c.IsPointInside(startPos + offset)))
                {
                    startPos += offset;
                }

                Vector2 endPos = startPos - Vector2.UnitY * Size.Y;

                if (Submarine.PickBody(
                    ConvertUnits.ToSimUnits(startPos),
                    ConvertUnits.ToSimUnits(endPos),
                    ExtraWalls.Where(w => w.Body?.BodyType == BodyType.Dynamic || w is DestructibleLevelWall).Select(w => w.Body), 
                    Physics.CollisionLevel | Physics.CollisionWall) != null)
                {
                    position = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition) + Vector2.Normalize(startPos - endPos) * offsetFromWall;
                    break;
                }

                tries++;

                if (tries == 10)
                {
                    position = EndPosition - Vector2.UnitY * 300.0f;
                }

            } while (tries < 10);

            return position;
        }

        public bool TryGetInterestingPosition(bool useSyncedRand, PositionType positionType, float minDistFromSubs, out Vector2 position, Func<InterestingPosition, bool> filter = null)
        {
            bool success = TryGetInterestingPosition(useSyncedRand, positionType, minDistFromSubs, out Point pos, filter);
            position = pos.ToVector2();
            return success;
        }

        public bool TryGetInterestingPosition(bool useSyncedRand, PositionType positionType, float minDistFromSubs, out Point position, Func<InterestingPosition, bool> filter = null)
        {
            if (!PositionsOfInterest.Any())
            {
                position = new Point(Size.X / 2, Size.Y / 2);
                return false;
            }

            List<InterestingPosition> suitablePositions = PositionsOfInterest.FindAll(p => positionType.HasFlag(p.PositionType));
            if (filter != null)
            {
                suitablePositions.RemoveAll(p => !filter(p));
            }
            //avoid floating ice chunks on the main path
            if (positionType.HasFlag(PositionType.MainPath) || positionType.HasFlag(PositionType.SidePath))
            {
                suitablePositions.RemoveAll(p => ExtraWalls.Any(w => w.Cells.Any(c => c.IsPointInside(p.Position.ToVector2()))));
            }
            if (!suitablePositions.Any())
            {
                string errorMsg = "Could not find a suitable position of interest. (PositionType: " + positionType + ", minDistFromSubs: " + minDistFromSubs + ")\n" + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("Level.TryGetInterestingPosition:PositionTypeNotFound", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#endif
                position = PositionsOfInterest[Rand.Int(PositionsOfInterest.Count, (useSyncedRand ? Rand.RandSync.Server : Rand.RandSync.Unsynced))].Position;
                return false;
            }

            List<InterestingPosition> farEnoughPositions = new List<InterestingPosition>(suitablePositions);
            if (minDistFromSubs > 0.0f)
            {
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub.Info.Type != SubmarineType.Player) { continue; }
                    farEnoughPositions.RemoveAll(p => Vector2.DistanceSquared(p.Position.ToVector2(), sub.WorldPosition) < minDistFromSubs * minDistFromSubs);
                }
            }
            if (!farEnoughPositions.Any())
            {
                string errorMsg = "Could not find a position of interest far enough from the submarines. (PositionType: " + positionType + ", minDistFromSubs: " + minDistFromSubs + ")\n" + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("Level.TryGetInterestingPosition:TooCloseToSubs", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#endif
                float maxDist = 0.0f;
                position = suitablePositions.First().Position;
                foreach (InterestingPosition pos in suitablePositions)
                {
                    float dist = Submarine.Loaded.Sum(s => 
                        Submarine.MainSubs.Contains(s) ? Vector2.DistanceSquared(s.WorldPosition, pos.Position.ToVector2()) : 0.0f);
                    if (dist > maxDist)
                    {
                        position = pos.Position;
                        maxDist = dist;
                    }
                }

                return false;
            }

            position = farEnoughPositions[Rand.Int(farEnoughPositions.Count, (useSyncedRand ? Rand.RandSync.Server : Rand.RandSync.Unsynced))].Position;
            return true;
        }

        public void Update(float deltaTime, Camera cam)
        {
            LevelObjectManager.Update(deltaTime);

            foreach (LevelWall wall in ExtraWalls) { wall.Update(deltaTime); }
            for (int i = UnsyncedExtraWalls.Count - 1; i >= 0; i--)
            {
                UnsyncedExtraWalls[i].Update(deltaTime);
            }

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                foreach (LevelWall wall in ExtraWalls) 
                {
                    if (wall is DestructibleLevelWall destructibleWall && destructibleWall.NetworkUpdatePending)
                    {
                        GameMain.NetworkMember.CreateEntityEvent(this, new object[] { destructibleWall });
                        destructibleWall.NetworkUpdatePending = false;
                    }
                }
                networkUpdateTimer += deltaTime;
                if (networkUpdateTimer > NetworkUpdateInterval)
                {
                    if (ExtraWalls.Any(w => w.Body.BodyType != BodyType.Static))
                    {
                        GameMain.NetworkMember.CreateEntityEvent(this);
                    }
                    networkUpdateTimer = 0.0f;
                }
            }

#if CLIENT
            backgroundCreatureManager.Update(deltaTime, cam);
            WaterRenderer.Instance?.ScrollWater(Vector2.UnitY, (float)deltaTime);
            renderer.Update(deltaTime, cam);
#endif
        }

        public Vector2 GetBottomPosition(float xPosition)
        {
            int index = (int)Math.Floor(xPosition / Size.X * (bottomPositions.Count - 1));
            if (index < 0 || index >= bottomPositions.Count - 1) return new Vector2(xPosition, BottomPos);

            float yPos = MathHelper.Lerp(
                bottomPositions[index].Y,
                bottomPositions[index + 1].Y,
                (xPosition - bottomPositions[index].X) / (bottomPositions[index + 1].X - bottomPositions[index].X));

            return new Vector2(xPosition, yPos);
        }
        
        public List<VoronoiCell> GetAllCells()
        {
            List<VoronoiCell> cells = new List<VoronoiCell>();
            for (int x = 0; x < cellGrid.GetLength(0); x++)
            {
                for (int y = 0; y < cellGrid.GetLength(1); y++)
                {
                    cells.AddRange(cellGrid[x, y]);
                }
            }
            return cells;
        }

        private readonly List<VoronoiCell> tempCells = new List<VoronoiCell>();
        public List<VoronoiCell> GetCells(Vector2 worldPos, int searchDepth = 2)
        {
            tempCells.Clear();
            int gridPosX = (int)Math.Floor(worldPos.X / GridCellSize);
            int gridPosY = (int)Math.Floor(worldPos.Y / GridCellSize);

            int startX = Math.Max(gridPosX - searchDepth, 0);
            int endX = Math.Min(gridPosX + searchDepth, cellGrid.GetLength(0) - 1);

            int startY = Math.Max(gridPosY - searchDepth, 0);
            int endY = Math.Min(gridPosY + searchDepth, cellGrid.GetLength(1) - 1);

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    tempCells.AddRange(cellGrid[x, y]);
                }
            }
            
            foreach (LevelWall wall in ExtraWalls)
            {
                if (wall is DestructibleLevelWall destructibleWall && destructibleWall.Destroyed) { continue; }
                foreach (VoronoiCell cell in wall.Cells)
                {
                    tempCells.Add(cell);
                }
            }
            
            return tempCells;
        }

        private void CreatePathToClosestTunnel(Point pos)
        {
            VoronoiCell closestPathCell = null;
            double closestDist = 0.0f;
            foreach (Tunnel tunnel in Tunnels)
            {
                if (tunnel.Type == TunnelType.Cave) { continue; }
                foreach (VoronoiCell cell in tunnel.Cells)
                {
                    double dist = MathUtils.DistanceSquared(cell.Site.Coord.X, cell.Site.Coord.Y, pos.X, pos.Y);
                    if (closestPathCell == null || dist < closestDist)
                    {
                        closestPathCell = cell;
                        closestDist = dist;
                    }
                }
            }

            //cast a ray from the closest path cell towards the position and remove the cells it hits
            List<VoronoiCell> validCells = cells.FindAll(c => c.CellType != CellType.Empty && c.CellType != CellType.Removed);
            foreach (VoronoiCell cell in validCells)
            {
                foreach (GraphEdge e in cell.Edges)
                {
                    if (!MathUtils.LinesIntersect(closestPathCell.Center, pos.ToVector2(), e.Point1, e.Point2)) { continue; }
                    
                    cell.CellType = CellType.Removed;
                    for (int x = 0; x < cellGrid.GetLength(0); x++)
                    {
                        for (int y = 0; y < cellGrid.GetLength(1); y++)
                        {
                            cellGrid[x, y].Remove(cell);
                        }
                    }
                    cells.Remove(cell);

                    //go through the edges of this cell and find the ones that are next to a removed cell
                    foreach (var otherEdge in cell.Edges)
                    {
                        var otherAdjacent = otherEdge.AdjacentCell(cell);
                        if (otherAdjacent == null || otherAdjacent.CellType == CellType.Solid) { continue; }

                        //if the edge is very short, remove adjacent cells to prevent making the passage too narrow
                        if (Vector2.DistanceSquared(otherEdge.Point1, otherEdge.Point2) < 500.0f * 500.0f)
                        {
                            foreach (GraphEdge e2 in cell.Edges)
                            {
                                if (e2 == otherEdge || e2 == otherEdge) { continue; }
                                if (!MathUtils.NearlyEqual(otherEdge.Point1, e2.Point1) && !MathUtils.NearlyEqual(otherEdge.Point2, e2.Point1) && !MathUtils.NearlyEqual(otherEdge.Point2, e2.Point2))
                                {
                                    continue;
                                }
                                var adjacentCell = e2.AdjacentCell(cell);
                                if (adjacentCell == null || adjacentCell.CellType == CellType.Removed) { continue; }
                                adjacentCell.CellType = CellType.Removed;
                                for (int x = 0; x < cellGrid.GetLength(0); x++)
                                {
                                    for (int y = 0; y < cellGrid.GetLength(1); y++)
                                    {
                                        cellGrid[x, y].Remove(adjacentCell);
                                    }
                                }
                                cells.Remove(adjacentCell);
                            }
                        }
                    }


                    break;
                    
                }
            }
        }

        public string GetWreckIDTag(string originalTag, Submarine wreck)
        {
            string shortSeed = ToolBox.StringToInt(LevelData.Seed + wreck?.Info.Name).ToString();
            if (shortSeed.Length > 6) { shortSeed = shortSeed.Substring(0, 6); }
            return originalTag + "_" + shortSeed;
        }

        private Submarine SpawnSubOnPath(string subName, ContentFile contentFile, SubmarineType type)
        {
            var tempSW = new Stopwatch();

            // Min distance between a sub and the start/end/other sub.
            float minDistance = Sonar.DefaultSonarRange;
            float squaredMinDistance = minDistance * minDistance;
            Vector2 start = startPosition.ToVector2();
            Vector2 end = endPosition.ToVector2();
            var waypoints = WayPoint.WayPointList.Where(wp =>
                wp.Submarine == null &&
                wp.SpawnType == SpawnType.Path &&
                Vector2.DistanceSquared(wp.WorldPosition, start) > squaredMinDistance &&
                Vector2.DistanceSquared(wp.WorldPosition, end) > squaredMinDistance).ToList();

            var subDoc = SubmarineInfo.OpenFile(contentFile.Path);
            Rectangle subBorders = Submarine.GetBorders(subDoc.Root);

            // Add some margin so that the sub doesn't block the path entirely. It's still possible that some larger subs can't pass by.
            Point paddedDimensions = new Point(subBorders.Width + 3000, subBorders.Height + 3000);

            var positions = new List<Vector2>();
            var rects = new List<Rectangle>();
            int maxAttempts = 50;
            int attemptsLeft = maxAttempts;
            bool success = false;
            Vector2 spawnPoint = Vector2.Zero;
            var allCells = Loaded.GetAllCells();
            while (attemptsLeft > 0)
            {
                if (attemptsLeft < maxAttempts)
                {
                    Debug.WriteLine($"Failed to position the sub {subName}. Trying again.");
                }
                attemptsLeft--;
                if (TryGetSpawnPoint(out spawnPoint))
                {
                    success = TryPositionSub(subBorders, subName, ref spawnPoint);
                    if (success)
                    {
                        break;
                    }
                    else
                    {
                        positions.Clear();
                    }
                }
                else
                {
                    DebugConsole.NewMessage($"Failed to find any spawn point for the sub: {subName} (No valid waypoints left).", Color.Red);
                    break;
                }
            }
            tempSW.Stop();
            if (success)
            {
                Debug.WriteLine($"Sub {subName} successfully positioned to {spawnPoint} in {tempSW.ElapsedMilliseconds} (ms)");
                tempSW.Restart();
                SubmarineInfo info = new SubmarineInfo(contentFile.Path)
                {
                    Type = type
                };
                Submarine sub = new Submarine(info);
                if (type == SubmarineType.Wreck)
                {
                    sub.MakeWreck();
                    Wrecks.Add(sub);
                    PositionsOfInterest.Add(new InterestingPosition(spawnPoint.ToPoint(), PositionType.Wreck, submarine: sub));
                    foreach (Hull hull in sub.GetHulls(false))
                    {
                        if (Rand.Value(Rand.RandSync.Server) <= Loaded.GenerationParams.WreckHullFloodingChance)
                        {
                            hull.WaterVolume = hull.Volume * Rand.Range(Loaded.GenerationParams.WreckFloodingHullMinWaterPercentage, Loaded.GenerationParams.WreckFloodingHullMaxWaterPercentage, Rand.RandSync.Server);
                        }
                    }
                    // Only spawn thalamus when the wreck has some thalamus items defined.
                    if (Rand.Value(Rand.RandSync.Server) <= Loaded.GenerationParams.ThalamusProbability && sub.GetItems(false).Any(i => i.Prefab.Category == MapEntityCategory.Thalamus))
                    {
                        if (!sub.CreateWreckAI())
                        {
                            DebugConsole.NewMessage($"Failed to create wreck AI inside {subName}.", Color.Red);
                            sub.DisableWreckAI();
                        }
                    }
                    else
                    {
                        sub.DisableWreckAI();
                    }
                }
                else if (type == SubmarineType.BeaconStation)
                {
                    sub.ShowSonarMarker = false;
                    sub.PhysicsBody.FarseerBody.BodyType = BodyType.Static;
                    sub.TeamID = CharacterTeamType.None;
                }
                tempSW.Stop();
                Debug.WriteLine($"Sub {sub.Info.Name} loaded in { tempSW.ElapsedMilliseconds} (ms)");
                sub.SetPosition(spawnPoint);
                wreckPositions.Add(sub, positions);
                blockedRects.Add(sub, rects);

                return sub;
            }
            else
            {
                DebugConsole.NewMessage($"Failed to position wreck {subName}. Used {tempSW.ElapsedMilliseconds.ToString()} (ms).", Color.Red);
                return null;
            }

            bool TryPositionSub(Rectangle subBorders, string subName, ref Vector2 spawnPoint)
            {
                positions.Add(spawnPoint);
                bool bottomFound = TryRaycastToBottom(subBorders, ref spawnPoint);
                positions.Add(spawnPoint);

                bool leftSideBlocked = IsSideBlocked(subBorders, false);
                bool rightSideBlocked = IsSideBlocked(subBorders, true);
                int step = 5;
                if (rightSideBlocked && !leftSideBlocked)
                {
                    bottomFound = TryMove(subBorders, ref spawnPoint, -step);
                }
                else if (leftSideBlocked && !rightSideBlocked)
                {
                    bottomFound = TryMove(subBorders, ref spawnPoint, step);
                }
                else if (!bottomFound)
                {
                    if (!leftSideBlocked)
                    {
                        bottomFound = TryMove(subBorders, ref spawnPoint, -step);
                    }
                    else if (!rightSideBlocked)
                    {
                        bottomFound = TryMove(subBorders, ref spawnPoint, step);
                    }
                    else
                    {
                        Debug.WriteLine($"Invalid position {spawnPoint}. Does not touch the ground.");
                        return false;
                    }
                }
                positions.Add(spawnPoint);
                bool isBlocked = IsBlocked(spawnPoint, subBorders.Size - new Point(step + 50));
                if (isBlocked)
                {
                    rects.Add(ToolBox.GetWorldBounds(spawnPoint.ToPoint(), subBorders.Size));
                    Debug.WriteLine($"Invalid position {spawnPoint}. Blocked by level walls.");
                }
                else if (!bottomFound)
                {
                    Debug.WriteLine($"Invalid position {spawnPoint}. Does not touch the ground.");
                }
                else
                {
                    var sp = spawnPoint;
                    if (Wrecks.Any(w => Vector2.DistanceSquared(w.WorldPosition, sp) < squaredMinDistance))
                    {
                        Debug.WriteLine($"Invalid position {spawnPoint}. Too close to other wreck(s).");
                        return false;
                    }
                }
                return !isBlocked && bottomFound;

                bool TryMove(Rectangle subBorders, ref Vector2 spawnPoint, float amount)
                {
                    float maxMovement = 5000;
                    float totalAmount = 0;
                    bool foundBottom = TryRaycastToBottom(subBorders, ref spawnPoint);
                    while (!IsSideBlocked(subBorders, amount > 0))
                    {
                        foundBottom = TryRaycastToBottom(subBorders, ref spawnPoint);
                        totalAmount += amount;
                        spawnPoint = new Vector2(spawnPoint.X + amount, spawnPoint.Y);
                        if (Math.Abs(totalAmount) > maxMovement)
                        {
                            Debug.WriteLine($"Moving the sub {subName} failed.");
                            break;
                        }
                    }
                    return foundBottom;
                }
            }

            bool TryGetSpawnPoint(out Vector2 spawnPoint)
            {
                spawnPoint = Vector2.Zero;
                while (waypoints.Any())
                {
                    var wp = waypoints.GetRandom(Rand.RandSync.Server);
                    waypoints.Remove(wp);
                    if (!IsBlocked(wp.WorldPosition, paddedDimensions))
                    {
                        spawnPoint = wp.WorldPosition;
                        return true;
                    }
                }
                return false;
            }

            bool TryRaycastToBottom(Rectangle subBorders, ref Vector2 spawnPoint)
            {
                // Shoot five rays and pick the highest hit point.
                int rayCount = 5;
                var positions = new Vector2[rayCount];
                bool hit = false;
                for (int i = 0; i < rayCount; i++)
                {
                    float quarterWidth = subBorders.Width * 0.25f;
                    Vector2 rayStart = spawnPoint;
                    switch (i)
                    {
                        case 1:
                            rayStart = new Vector2(spawnPoint.X - quarterWidth, spawnPoint.Y);
                            break;
                        case 2:
                            rayStart = new Vector2(spawnPoint.X + quarterWidth, spawnPoint.Y);
                            break;
                        case 3:
                            rayStart = new Vector2(spawnPoint.X - quarterWidth / 2, spawnPoint.Y);
                            break;
                        case 4:
                            rayStart = new Vector2(spawnPoint.X + quarterWidth / 2, spawnPoint.Y);
                            break;
                    }
                    var simPos = ConvertUnits.ToSimUnits(rayStart);
                    var body = Submarine.PickBody(simPos, new Vector2(simPos.X, -1),
                        customPredicate: f => f.Body?.UserData is VoronoiCell cell && cell.Body.BodyType == BodyType.Static && !ExtraWalls.Any(w => w.Body == f.Body),
                        collisionCategory: Physics.CollisionLevel | Physics.CollisionWall);
                    if (body != null)
                    {
                        positions[i] = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition) + new Vector2(0, subBorders.Height / 2);
                        hit = true;
                    }
                }
                float highestPoint = positions.Max(p => p.Y);
                spawnPoint = new Vector2(spawnPoint.X, highestPoint);
                return hit;
            }

            bool IsSideBlocked(Rectangle subBorders, bool front)
            {
                // Shoot three rays and check whether any of them hits.
                int rayCount = 3;
                Vector2 halfSize = subBorders.Size.ToVector2() / 2;
                Vector2 quarterSize = halfSize / 2;
                var positions = new Vector2[rayCount];
                for (int i = 0; i < rayCount; i++)
                {
                    float dir = front ? 1 : -1;
                    Vector2 rayStart;
                    Vector2 to;
                    switch (i)
                    {
                        case 1:
                            rayStart = new Vector2(spawnPoint.X + halfSize.X * dir, spawnPoint.Y + quarterSize.Y);
                            to = new Vector2(spawnPoint.X + (halfSize.X - quarterSize.X) * dir, rayStart.Y);
                            break;
                        case 2:
                            rayStart = new Vector2(spawnPoint.X + halfSize.X * dir, spawnPoint.Y - quarterSize.Y);
                            to = new Vector2(spawnPoint.X + (halfSize.X - quarterSize.X) * dir, rayStart.Y);
                            break;
                        case 0:
                        default:
                            rayStart = spawnPoint;
                            to = new Vector2(spawnPoint.X + halfSize.X * dir, rayStart.Y);
                            break;
                    }
                    Vector2 simPos = ConvertUnits.ToSimUnits(rayStart);
                    if (Submarine.PickBody(simPos, ConvertUnits.ToSimUnits(to),
                        customPredicate: f => f.Body?.UserData is VoronoiCell cell,
                        collisionCategory: Physics.CollisionLevel | Physics.CollisionWall) != null)
                    {
                        return true;
                    }
                }
                return false;
            }

            bool IsBlocked(Vector2 pos, Point size, float maxDistanceMultiplier = 1)
            {
                float maxDistance = size.Multiply(maxDistanceMultiplier).ToVector2().LengthSquared();
                Rectangle bounds = ToolBox.GetWorldBounds(pos.ToPoint(), size);
                if (Ruins.Any(r => ToolBox.GetWorldBounds(r.Area.Center, r.Area.Size).IntersectsWorld(bounds)))
                {
                    return true;
                }
                if (Caves.Any(c => 
                        ToolBox.GetWorldBounds(c.Area.Center, c.Area.Size).IntersectsWorld(bounds) || 
                        ToolBox.GetWorldBounds(c.StartPos, new Point(1500)).IntersectsWorld(bounds)))
                {
                    return true;
                }
                return cells.Any(c => c.Body != null && Vector2.DistanceSquared(pos, c.Center) <= maxDistance && c.BodyVertices.Any(v => bounds.ContainsWorld(v)));
            }
        }

        // For debugging
        private readonly Dictionary<Submarine, List<Vector2>> wreckPositions = new Dictionary<Submarine, List<Vector2>>();
        private readonly Dictionary<Submarine, List<Rectangle>> blockedRects = new Dictionary<Submarine, List<Rectangle>>();
        private void CreateWrecks()
        {
            var totalSW = new Stopwatch();
            totalSW.Start();
            var wreckFiles = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.Wreck).ToList();
            if (wreckFiles.None())
            {
                DebugConsole.ThrowError("No wreck files found in the selected content packages!");
                return;
            }
            wreckFiles.Shuffle(Rand.RandSync.Server);

            int wreckCount = Math.Min(Loaded.GenerationParams.WreckCount, wreckFiles.Count);
            Wrecks = new List<Submarine>(wreckCount);
            for (int i = 0; i < wreckCount; i++)
            {
                ContentFile contentFile = wreckFiles[i];
                if (contentFile == null) { continue; }
                string wreckName = System.IO.Path.GetFileNameWithoutExtension(contentFile.Path);
                // For storing the translations. Used only for debugging.
                SpawnSubOnPath(wreckName, contentFile, SubmarineType.Wreck);
            }
            totalSW.Stop();
            Debug.WriteLine($"{Wrecks.Count} wrecks created in { totalSW.ElapsedMilliseconds.ToString()} (ms)");
        }

        private void CreateOutposts()
        {
            var outpostFiles = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.Outpost).ToList();
            if (!outpostFiles.Any() && !OutpostGenerationParams.Params.Any() && LevelData.ForceOutpostGenerationParams == null)
            {
                DebugConsole.ThrowError("No outpost files found in the selected content packages");
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                if (Submarine.MainSubs.Length > 1 && Submarine.MainSubs[0] != null && Submarine.MainSubs[1] != null)
                {
                    continue;
                }

                bool isStart = (i == 0) == !Mirrored;
                if (isStart)
                {
                    //only create a starting outpost in campaign and tutorial modes
#if CLIENT       
                    if (Screen.Selected != GameMain.LevelEditorScreen && !IsModeStartOutpostCompatible())
                    {
                        continue;
                    }
#else
                    if (!IsModeStartOutpostCompatible())
                    {
                        continue;
                    }
#endif
                    if (StartLocation != null && !StartLocation.Type.HasOutpost) { continue; }
                }
                else
                {
                    //don't create an end outpost for locations
                    if (LevelData.Type == LevelData.LevelType.Outpost) { continue; }
                    if (EndLocation != null && !EndLocation.Type.HasOutpost) { continue; }
                }

                SubmarineInfo outpostInfo;
                Submarine outpost;
                if (i == 0 && preSelectedStartOutpost == null || i == 1 && preSelectedEndOutpost == null)
                {
                    if (OutpostGenerationParams.Params.Any() || LevelData.ForceOutpostGenerationParams != null)
                    {
                        Location location = i == 0 ? StartLocation : EndLocation;

                        OutpostGenerationParams outpostGenerationParams = null;
                        if (LevelData.ForceOutpostGenerationParams != null)
                        {
                            outpostGenerationParams = LevelData.ForceOutpostGenerationParams;
                        }
                        else
                        {
                            var suitableParams = OutpostGenerationParams.Params
                                .Where(p => location == null || p.AllowedLocationTypes.Contains(location.Type.Identifier));
                            if (suitableParams.Count() == 0)
                            {
                                suitableParams = OutpostGenerationParams.Params
                                    .Where(p => location == null || !p.AllowedLocationTypes.Any());
                            }

                            if (!suitableParams.Any())
                            {
                                DebugConsole.ThrowError("No suitable outpost generation parameters found for the location type \"" + location.Type.Identifier + "\". Selecting random parameters.");
                                suitableParams = OutpostGenerationParams.Params;
                            }
                            outpostGenerationParams = suitableParams.GetRandom(Rand.RandSync.Server);
                        }

                        LocationType locationType = location?.Type;
                        if (locationType == null)
                        {
                            locationType = LocationType.List.GetRandom(Rand.RandSync.Server);
                            if (outpostGenerationParams.AllowedLocationTypes.Any())
                            {
                                locationType = LocationType.List.Where(lt => 
                                    outpostGenerationParams.AllowedLocationTypes.Any(allowedType => 
                                      allowedType.Equals("any", StringComparison.OrdinalIgnoreCase) || lt.Identifier.Equals(allowedType, StringComparison.OrdinalIgnoreCase))).GetRandom();
                            }
                        }

                        if (location != null)
                        {
                            DebugConsole.NewMessage($"Generating an outpost for the {(isStart ? "start" : "end")} of the level... (Location: {location.Name}, level type: {LevelData.Type})");
                            outpost = OutpostGenerator.Generate(outpostGenerationParams, location, onlyEntrance: LevelData.Type != LevelData.LevelType.Outpost);
                        }
                        else
                        {
                            DebugConsole.NewMessage($"Generating an outpost for the {(isStart ? "start" : "end")} of the level... (Location type: {locationType}, level type: {LevelData.Type})");
                            outpost = OutpostGenerator.Generate(outpostGenerationParams, locationType, onlyEntrance: LevelData.Type != LevelData.LevelType.Outpost);
                        }
                    }
                    else
                    {
                        DebugConsole.NewMessage($"Loading a pre-built outpost for the {(isStart ? "start" : "end")} of the level...");
                        //backwards compatibility: if there are no generation params available, try to load an outpost file saved as a sub
                        ContentFile outpostFile = outpostFiles.GetRandom(Rand.RandSync.Server);
                        outpostInfo = new SubmarineInfo(outpostFile.Path)
                        {
                            Type = SubmarineType.Outpost
                        };
                        outpost = new Submarine(outpostInfo);
                    }
                }
                else
                {
                    DebugConsole.NewMessage($"Loading a pre-selected outpost for the {(isStart ? "start" : "end")} of the level...");
                    outpostInfo = (i == 0) ? preSelectedStartOutpost : preSelectedEndOutpost;
                    outpostInfo.Type = SubmarineType.Outpost;
                    outpost = new Submarine(outpostInfo);
                }

                Point? minSize = null;
                DockingPort subPort = null;
                float closestDistance = float.MaxValue;
                if (Submarine.MainSub != null)
                {
                    Point subSize = Submarine.MainSub.GetDockedBorders().Size;
                    Point outpostSize = outpost.GetDockedBorders().Size;
                    minSize = new Point(Math.Max(subSize.X, outpostSize.X), subSize.Y + outpostSize.Y);

                    foreach (DockingPort port in DockingPort.List)
                    {
                        if (port.IsHorizontal || port.Docked) { continue; }
                        if (port.Item.Submarine != Submarine.MainSub) { continue; }
                        //the submarine port has to be at the top of the sub
                        if (port.Item.WorldPosition.Y < Submarine.MainSub.WorldPosition.Y) { continue; }
                        float dist = Math.Abs(port.Item.WorldPosition.X - Submarine.MainSub.WorldPosition.X);
                        if (dist < closestDistance)
                        {
                            subPort = port;
                            closestDistance = dist;
                        }
                    }
                }

                DockingPort outpostPort = null;
                closestDistance = float.MaxValue;
                foreach (DockingPort port in DockingPort.List)
                {
                    if (port.IsHorizontal || port.Docked) { continue; }
                    if (port.Item.Submarine != outpost) { continue; }
                    //the outpost port has to be at the bottom of the outpost
                    if (port.Item.WorldPosition.Y > outpost.WorldPosition.Y) { continue; }
                    float dist = Math.Abs(port.Item.WorldPosition.X - outpost.WorldPosition.X);
                    if (dist < closestDistance)
                    {
                        outpostPort = port;
                        closestDistance = dist;
                    }
                }

                float subDockingPortOffset = subPort == null ? 0.0f : subPort.Item.WorldPosition.X - Submarine.MainSub.WorldPosition.X;
                //don't try to compensate if the port is very far from the sub's center of mass
                if (Math.Abs(subDockingPortOffset) > 5000.0f)
                {
                    subDockingPortOffset = MathHelper.Clamp(subDockingPortOffset, -5000.0f, 5000.0f);
                    string warningMsg = "Docking port very far from the sub's center of mass (submarine: " + Submarine.MainSub.Info.Name + ", dist: " + subDockingPortOffset + "). The level generator may not be able to place the outpost so that docking is possible.";
                    DebugConsole.NewMessage(warningMsg, Color.Orange);
                    GameAnalyticsManager.AddErrorEventOnce("Lever.CreateOutposts:DockingPortVeryFar" + Submarine.MainSub.Info.Name, GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, warningMsg);
                }

                float outpostDockingPortOffset = subPort == null ? 0.0f : outpostPort.Item.WorldPosition.X - outpost.WorldPosition.X;
                //don't try to compensate if the port is very far from the outpost's center of mass
                if (Math.Abs(outpostDockingPortOffset) > 5000.0f)
                {
                    outpostDockingPortOffset = MathHelper.Clamp(outpostDockingPortOffset, -5000.0f, 5000.0f);
                    string warningMsg = "Docking port very far from the outpost's center of mass (outpost: " + outpost.Info.Name + ", dist: " + outpostDockingPortOffset + "). The level generator may not be able to place the outpost so that docking is possible.";
                    DebugConsole.NewMessage(warningMsg, Color.Orange);
                    GameAnalyticsManager.AddErrorEventOnce("Lever.CreateOutposts:OutpostDockingPortVeryFar" + outpost.Info.Name, GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, warningMsg);
                }

                Vector2 spawnPos = outpost.FindSpawnPos(i == 0 ? StartPosition : EndPosition, minSize, subDockingPortOffset - outpostDockingPortOffset, verticalMoveDir: 1);
                if (Type == LevelData.LevelType.Outpost)
                {
                    spawnPos.Y = Math.Min(Size.Y - outpost.Borders.Height * 0.6f, spawnPos.Y + outpost.Borders.Height / 2);
                }
                outpost.SetPosition(spawnPos);
                if ((i == 0) == !Mirrored)
                {
                    StartOutpost = outpost;
                    if (StartLocation != null) { outpost.Info.Name = StartLocation.Name; }
                }
                else
                {
                    EndOutpost = outpost;
                    if (EndLocation != null) { outpost.Info.Name = EndLocation.Name; }
                }
            }
        }

        private void CreateBeaconStation(List<VoronoiCell> mainPath)
        {
            if (!LevelData.HasBeaconStation) { return; }
            var beaconStationFiles = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.BeaconStation).ToList();
            if (beaconStationFiles.None())
            {
                DebugConsole.ThrowError("No BeaconStation files found in the selected content packages!");
                return;
            }
            var contentFile = beaconStationFiles.GetRandom(Rand.RandSync.Server);
            string beaconStationName = System.IO.Path.GetFileNameWithoutExtension(contentFile.Path);

            BeaconStation = SpawnSubOnPath(beaconStationName, contentFile, SubmarineType.BeaconStation);
            if (BeaconStation == null) { return; }

            Item sonarItem = Item.ItemList.Find(it => it.Submarine == BeaconStation && it.GetComponent<Sonar>() != null);
            if (sonarItem == null)
            {
                DebugConsole.ThrowError($"No sonar found in the beacon station \"{beaconStationName}\"!");
                return;
            }
            beaconSonar = sonarItem.GetComponent<Sonar>();
        }

        public void PrepareBeaconStation()
        {
            if (!LevelData.HasBeaconStation) { return; }
            if (GameMain.NetworkMember?.IsClient ?? false) { return; }

            List<Item> beaconItems = Item.ItemList.FindAll(it => it.Submarine == BeaconStation);

            Item reactorItem = beaconItems.Find(it => it.GetComponent<Reactor>() != null);
            Reactor reactorComponent = reactorItem.GetComponent<Reactor>();
            ItemContainer reactorContainer = reactorItem.GetComponent<ItemContainer>();

            if (LevelData.IsBeaconActive)
            {
                if (reactorContainer.Inventory.IsEmpty())
                {
                    ItemPrefab fuelPrefab = ItemPrefab.Prefabs[reactorContainer.ContainableItems[0].Identifiers[0]];
                    Entity.Spawner.AddToSpawnQueue(
                        fuelPrefab, reactorContainer.Inventory,
                        onSpawned: (it) => reactorComponent.PowerUpImmediately());
                }
                beaconSonar.CurrentMode = Sonar.Mode.Active;
#if SERVER
                beaconSonar.Item.CreateServerEvent(beaconSonar);
#endif
            }
            else
            {
                if (!(GameMain.NetworkMember?.IsClient ?? false))
                {
                    //empty the reactor
                    foreach (Item item in reactorContainer.Inventory.AllItems)
                    {
                        if (item.NonInteractable) { continue; }
                        Entity.Spawner.AddToRemoveQueue(item);
                    }

                    //remove wires
                    foreach (Item item in beaconItems.Where(it => it.GetComponent<Wire>() != null).ToList())
                    {
                        if (item.NonInteractable) { continue; }
                        Wire wire = item.GetComponent<Wire>();
                        if (wire.Locked) { continue; }
                        if (wire.Connections[0] != null && (wire.Connections[0].Item.NonInteractable || wire.Connections[0].Item.GetComponent<ConnectionPanel>().Locked))
                        {
                            continue;
                        }
                        if (wire.Connections[1] != null && (wire.Connections[1].Item.NonInteractable || wire.Connections[1].Item.GetComponent<ConnectionPanel>().Locked))
                        {
                            continue;
                        }
                        if (Rand.Range(0f, 1f, Rand.RandSync.Unsynced) < 0.25f)
                        {
                            Entity.Spawner.AddToRemoveQueue(item);
                        }
                    }

                    //break powered items
                    foreach (Item item in beaconItems.Where(it => it.Components.Any(c => c is Powered)))
                    {
                        if (item.NonInteractable) { continue; }
                        if (Rand.Range(0f, 1f, Rand.RandSync.Unsynced) < 0.5f)
                        {
                            item.Condition *= Rand.Range(0.2f, 0.6f, Rand.RandSync.Unsynced);
                        }
                    }

                    //poke holes in the walls
                    foreach (Structure structure in Structure.WallList.Where(s => s.Submarine == BeaconStation))
                    {
                        if (Rand.Range(0f, 1f, Rand.RandSync.Unsynced) < 0.25f)
                        {
                            int sectionIndex = Rand.Range(0, structure.SectionCount - 1, Rand.RandSync.Unsynced);
                            structure.AddDamage(sectionIndex, Rand.Range(structure.MaxHealth * 0.2f, structure.MaxHealth, Rand.RandSync.Unsynced));
                        }
                    }
                }
            }
        }

        public bool CheckBeaconActive()
        {
            if (beaconSonar == null) { return false; }
            return beaconSonar.Voltage > beaconSonar.MinVoltage && beaconSonar.CurrentMode == Sonar.Mode.Active;
        }

        private bool IsModeStartOutpostCompatible()
        {
#if CLIENT
            return GameMain.GameSession?.GameMode is CampaignMode || GameMain.GameSession?.GameMode is TutorialMode || GameMain.GameSession?.GameMode is TestGameMode;
#else
            return GameMain.GameSession?.GameMode is CampaignMode;
#endif
        }

        public void SpawnCorpses()
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            foreach (Submarine wreck in Wrecks)
            {
                int corpseCount = Rand.Range(Loaded.GenerationParams.MinCorpseCount, Loaded.GenerationParams.MaxCorpseCount);
                var allSpawnPoints = WayPoint.WayPointList.FindAll(wp => wp.Submarine == wreck && wp.CurrentHull != null);
                var pathPoints = allSpawnPoints.FindAll(wp => wp.SpawnType == SpawnType.Path);
                pathPoints.Shuffle(Rand.RandSync.Unsynced);
                var corpsePoints = allSpawnPoints.FindAll(wp => wp.SpawnType == SpawnType.Corpse);
                corpsePoints.Shuffle(Rand.RandSync.Unsynced);
                int spawnCounter = 0;
                for (int j = 0; j < corpseCount; j++)
                {
                    WayPoint sp = corpsePoints.FirstOrDefault() ?? pathPoints.FirstOrDefault();
                    JobPrefab job = sp?.AssignedJob;
                    CorpsePrefab selectedPrefab;
                    if (job == null)
                    {
                        selectedPrefab = GetCorpsePrefab(p => p.SpawnPosition == PositionType.Wreck);
                    }
                    else
                    {
                        selectedPrefab = GetCorpsePrefab(p => p.SpawnPosition == PositionType.Wreck && (p.Job == "any" || p.Job == job.Identifier));
                        if (selectedPrefab == null)
                        {
                            corpsePoints.Remove(sp);
                            pathPoints.Remove(sp);
                            sp = corpsePoints.FirstOrDefault(sp => sp.AssignedJob == null) ?? pathPoints.FirstOrDefault(sp => sp.AssignedJob == null);
                            // Deduce the job from the selected prefab
                            selectedPrefab = GetCorpsePrefab(p => p.SpawnPosition == PositionType.Wreck);
                        }
                    }
                    if (selectedPrefab == null) { continue; }
                    Vector2 worldPos;
                    if (sp == null)
                    {
                        if (!TryGetExtraSpawnPoint(out worldPos))
                        {
                            break;
                        }
                    }
                    else
                    {
                        worldPos = sp.WorldPosition;
                        corpsePoints.Remove(sp);
                        pathPoints.Remove(sp);
                    }

                    job ??= selectedPrefab.GetJobPrefab();
                    if (job == null) { continue; }

                    var characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: job, randSync: Rand.RandSync.Server);
                    var corpse = Character.Create(CharacterPrefab.HumanConfigFile, worldPos, ToolBox.RandomSeed(8), characterInfo, hasAi: true, createNetworkEvent: true);
                    corpse.AnimController.FindHull(worldPos, true);
                    corpse.TeamID = CharacterTeamType.None;
                    corpse.EnableDespawn = false;
                    selectedPrefab.GiveItems(corpse, wreck);
                    corpse.Kill(CauseOfDeathType.Unknown, causeOfDeathAffliction: null, log: false);
                    spawnCounter++;

                    static CorpsePrefab GetCorpsePrefab(Func<CorpsePrefab, bool> predicate)
                    {
                        IEnumerable<CorpsePrefab> filteredPrefabs = CorpsePrefab.Prefabs.Where(predicate);
                        return ToolBox.SelectWeightedRandom(filteredPrefabs.ToList(), filteredPrefabs.Select(p => p.Commonness).ToList(), Rand.RandSync.Unsynced);
                    }
                }
#if DEBUG
                DebugConsole.NewMessage($"{spawnCounter}/{corpseCount} corpses spawned in {wreck.Info.Name}.", spawnCounter == corpseCount ? Color.Green : Color.Yellow);
#endif
                bool TryGetExtraSpawnPoint(out Vector2 point)
                {
                    point = Vector2.Zero;
                    var hull = Hull.hullList.FindAll(h => h.Submarine == wreck).GetRandom();
                    if (hull != null)
                    {
                        point = hull.WorldPosition;
                    }
                    return hull != null;
                }
            }
        }

        public void SpawnNPCs()
        {
            if (Type != LevelData.LevelType.Outpost) { return; }
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub?.Info?.OutpostGenerationParams != null)
                {
                    OutpostGenerator.SpawnNPCs((GameMain.GameSession?.GameMode as CampaignMode)?.Map?.CurrentLocation, sub);
                }
            }
        }

        /// <summary>
        /// Calculate the "real" depth in meters from the surface of Europa
        /// </summary>
        public float GetRealWorldDepth(float worldPositionY)
        {
            return (-(worldPositionY - GenerationParams.Height) + LevelData.InitialDepth) * Physics.DisplayToRealWorldRatio;            
        }

        public void DebugSetStartLocation(Location newStartLocation)
        {
            StartLocation = newStartLocation;
        }

        public void DebugSetEndLocation(Location newEndLocation)
        {
            EndLocation = newEndLocation;
        }

        public override void Remove()
        {
            base.Remove();
#if CLIENT
            if (renderer != null) 
            {
                renderer.Dispose();
                renderer = null;
            }
#endif

            if (LevelObjectManager != null)
            {
                LevelObjectManager.Remove();
                LevelObjectManager = null;
            }

            if (Ruins != null)
            {
                Ruins.Clear();
                Ruins = null;
            }

            if (ExtraWalls != null)
            {
                foreach (LevelWall w in ExtraWalls) { w.Dispose(); }
                ExtraWalls = null;
            }
            if (UnsyncedExtraWalls != null)
            {
                foreach (LevelWall w in UnsyncedExtraWalls) { w.Dispose(); }
                UnsyncedExtraWalls = null;
            }

            cells = null;
            
            if (bodies != null)
            {
                bodies.Clear();
                bodies = null;
            }

            Loaded = null;
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            if (extraData != null && extraData.Length > 0 && extraData[0] is DestructibleLevelWall destructibleWall)
            {
                int index = ExtraWalls.IndexOf(destructibleWall);
                msg.Write(false);
                msg.Write((ushort)(index == -1 ? ushort.MaxValue : index));
                //write health using one byte
                msg.Write((byte)MathHelper.Clamp((int)(MathUtils.InverseLerp(0.0f, destructibleWall.MaxHealth, destructibleWall.Damage) * 255.0f), 0, 255));
            }
            else
            {
                msg.Write(true);
                foreach (LevelWall levelWall in ExtraWalls)
                {
                    if (levelWall.Body.BodyType == BodyType.Static) { continue; }
                    msg.Write(levelWall.Body.Position.X);
                    msg.Write(levelWall.Body.Position.Y);
                    msg.WriteRangedSingle(levelWall.MoveState, 0.0f, MathHelper.TwoPi, 16);                    
                }
            }
        }
    }
}
