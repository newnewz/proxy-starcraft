﻿using System;
using System.Collections.Generic;
using System.Linq;
using ProxyStarcraft.Maps;

namespace ProxyStarcraft.Basic
{
    public class BasicMapAnalyzer : IMapAnalyzer<BasicMapData>
    {
        public BasicMapData Get(BasicMapData prior, Map map)
        {
            var deposits = GetDeposits(map.StructuresAndDeposits, prior.Areas, prior.AreaGrid);
            return new BasicMapData(prior.Areas, prior.AreaGrid, deposits);
        }

        public BasicMapData GetInitial(Map map)
        {
            var areaGrid = GetAreaGrid(map);
            var areas = GetAreas(map, areaGrid);
            var deposits = GetDeposits(map.StructuresAndDeposits, areas, areaGrid);
            return new BasicMapData(areas, areaGrid, deposits);
        }

        /// <summary>
        /// Builds a list of <see cref="Area"/>s that have references to their neighbors.
        /// </summary>
        private List<Area> GetAreas(Map map, MapArray<byte> areaGrid) // TODO: Break up this function
        {
            var neighbors = GetNeighbors(areaGrid);

            var mesas = new Dictionary<byte, Mesa>();
            var ramps = new Dictionary<byte, Ramp>();
            var edges = new Dictionary<byte, Edge>();

            var maxAreaId = areaGrid.Data.Max();

            // Need to get the center of each area. To avoid translation confusion,
            // I'm just going to create arrays with an extra space even though there's
            // no relevant area 0 (which instead represents impassible terrain).
            var centers = new Location[maxAreaId + 1];
            var locationLists = new List<Location>[maxAreaId + 1];
            var xTotals = new int[maxAreaId + 1];
            var yTotals = new int[maxAreaId + 1];
            var counts = new int[maxAreaId + 1];

            for (var x = 0; x < map.Size.X; x++)
            {
                for (var y = 0; y < map.Size.Y; y++)
                {
                    var areaId = areaGrid[x, y];
                    if (areaId != 0)
                    {
                        xTotals[areaId] += x;
                        yTotals[areaId] += y;
                        counts[areaId] += 1;

                        locationLists[areaId] = locationLists[areaId] ?? new List<Location>();
                        locationLists[areaId].Add(new Location { X = x, Y = y });
                    }
                }
            }

            // Build mesas first - ramps need mesa info in their constructor,
            // and for convenience mesas can add neighbors post-construction
            for (byte areaId = 1; areaId <= maxAreaId; areaId++)
            {
                // This part can really be done once for both ramps and mesas
                var center = new Location
                {
                    X = xTotals[areaId] / counts[areaId],
                    Y = yTotals[areaId] / counts[areaId]
                };

                if (locationLists[areaId].Contains(center))
                {
                    centers[areaId] = center;
                }
                else
                {
                    centers[areaId] = center.GetClosest(locationLists[areaId]);
                }

                if (map.CanBuild(locationLists[areaId][0]))
                {
                    var height = map.HeightGrid[locationLists[areaId][0]];

                    mesas[areaId] = new Mesa(areaId, locationLists[areaId], centers[areaId], height);
                }
            }

            for (byte areaId = 1; areaId <= maxAreaId; areaId++)
            {
                if (!map.CanBuild(locationLists[areaId][0]))
                {
                    var topAndBottomNeighborIds =
                        neighbors
                            .Where(pair => pair.Item1 == areaId || pair.Item2 == areaId)
                            .Select(pair => pair.Item1 == areaId ? pair.Item2 : pair.Item1).ToList();

                    if (topAndBottomNeighborIds.Count != 2)
                    {
                        // This isn't really a ramp. Using the 'Edge' class for miscellaneous non-buildable
                        // areas for now. This does assume that an Edge won't connect to a Ramp.
                        var neighborMesas = topAndBottomNeighborIds.Select(id => mesas[id]).ToArray();
                        edges[areaId] = new Edge(areaId, locationLists[areaId], centers[areaId], neighborMesas);

                        foreach (var neighborMesa in neighborMesas)
                        {
                            neighborMesa.AddNeighbor(edges[areaId]);
                        }
                    }
                    else
                    {
                        var topAndBottomNeighbors = topAndBottomNeighborIds.Select(id => mesas[id]).OrderBy(mesa => mesa.Height).ToArray();

                        ramps[areaId] = new Ramp(areaId, locationLists[areaId], centers[areaId], topAndBottomNeighbors[1], topAndBottomNeighbors[0]);
                        mesas[topAndBottomNeighborIds[0]].AddNeighbor(ramps[areaId]);
                        mesas[topAndBottomNeighborIds[1]].AddNeighbor(ramps[areaId]);
                    }
                }
            }

            // At some point there might be 'mesas' that are adjacent, because we want to
            // subdivide large areas with multiple mining locations even if there's no ramp.
            foreach (var neighbor in neighbors)
            {
                if (mesas.ContainsKey(neighbor.Item1) && mesas.ContainsKey(neighbor.Item2))
                {
                    mesas[neighbor.Item1].AddNeighbor(mesas[neighbor.Item2]);
                    mesas[neighbor.Item2].AddNeighbor(mesas[neighbor.Item1]);
                }
            }

            return mesas.Values.Concat<Area>(ramps.Values).ToList();
        }

        /// <summary>
        /// Builds a representation of the map where each distinct area has a different value.
        /// </summary>
        /// <returns>A represntation of the map where all spaces that are the same 'area' have the same numeric value.
        /// Locations that are not accessible to ground units have a value of 0.</returns>
        private MapArray<byte> GetAreaGrid(Map map)
        {
            /* Pick a starting location and fan out to adjacent locations.
             * 
             * If it's a buildable zone, find all spots that are also buildable,
             * adjacent, and at the same height. This will be a 'mesa'.
             * 
             * If it's not buildable but it can be traversed then it's a ramp,
             * find all adjacent spots that can also be traversed and are not buildable.
             *
             *  We can probably use the starting locations on the map as safe places to begin,
             *  since they'll be base locations.
             */

            // TODO: Add support for "islands" - this mechanism will only capture areas connected to the starting location

            // Resulting array of areas
            MapArray<byte> areas = new MapArray<byte>(map.Size);

            // Area id - will be incremented as we move to other areas
            // (and before assigning the first area as well)
            byte currentId = 0;

            var locations = new HashSet<Location>();
            var otherAreaLocations = new HashSet<Location>();

            var startPosition = map.Raw.StartLocations[0];

            otherAreaLocations.Add(new Location { X = (int)startPosition.X, Y = (int)startPosition.Y });

            while (otherAreaLocations.Count > 0)
            {
                var lastLocation = otherAreaLocations.First();
                otherAreaLocations.Remove(lastLocation);
                currentId++;
                areas[lastLocation] = currentId;

                AddAdjacentLocations(map, lastLocation, locations, areas);

                while (locations.Count > 0)
                {
                    var location = locations.First();
                    locations.Remove(location);
                    otherAreaLocations.Remove(location);

                    if (map.CanBuild(lastLocation) == map.CanBuild(location))
                    {
                        areas[location] = currentId;
                        AddAdjacentLocations(map, location, locations, areas);
                    }
                    else
                    {
                        otherAreaLocations.Add(location);
                    }
                }
            }

            return areas;
        }

        /// <summary>
        /// Builds a list of resource deposits. Requires that the 'areas' and 'areaGrid' fields be set.
        /// </summary>
        private List<Deposit> GetDeposits(IReadOnlyList<Unit> units, IReadOnlyList<Area> areas, MapArray<byte> areaGrid)
        {
            var resourcesByArea = units.Where(u => u.IsMineralDeposit || u.IsVespeneGeyser)
                                       .GroupBy(m => areaGrid[(int)m.X, (int)m.Y]);

            var deposits = new List<Deposit>();

            foreach (var resourceArea in resourcesByArea)
            {
                var resources = resourceArea.ToList();

                while (resources.Count > 0)
                {
                    var depositResources = new List<Unit>();
                    var nextResource = resources[0];
                    resources.RemoveAt(0);

                    while (nextResource != null)
                    {
                        depositResources.Add(nextResource);
                        resources.Remove(nextResource);

                        nextResource = resources.FirstOrDefault(r => depositResources.Any(d => r.GetDistance(d) < 5f));
                    }

                    var area = areas.First(a => a.Id == areaGrid[(int)depositResources[0].X, (int)depositResources[0].Y]);
                    var center = new Location
                    {
                        X = (int)(depositResources.Sum(u => u.X) / depositResources.Count),
                        Y = (int)(depositResources.Sum(u => u.Y) / depositResources.Count)
                    };

                    deposits.Add(new Deposit(area, center, depositResources));
                }
            }

            return deposits;
        }


        /// <summary>
        /// Determines which areas are neighbors of other areas.
        /// </summary>
        /// <param name="areas">A map representation where each contiguous area has the same number (i.e. the output of <see cref="GetAreaGrid"/>).</param>
        /// <returns>A distinct set of neighbor relationships as byte-tuples (with the lower-value id coming first in each tuple).</returns>
        private static HashSet<(byte, byte)> GetNeighbors(MapArray<byte> areas)
        {
            /* Neighbors are areas with bordering spaces. I'm pretty sure we don't
             * have to worry about diagonals because I don't think they come up specifically,
             * and I don't think you can move between two diagonal neighbor spaces if there
             * are no other open spaces around.
             * 
             * As such, this just goes from left-to-right in each row and bottom-to-top
             * in each column and whenever the adjacent numbers are different and non-zero,
             * that's a neighboring relationship.
             */
            var neighbors = new HashSet<(byte, byte)>();

            // TODO: Get rid of code duplication
            for (var x = 0; x < areas.Size.X; x++)
            {
                for (var y = 1; y < areas.Size.Y; y++)
                {
                    if (areas[x, y - 1] != 0 && areas[x, y] != 0 && areas[x, y - 1] != areas[x, y])
                    {
                        var first = areas[x, y - 1];
                        var second = areas[x, y];

                        // Consistently put the lesser number first to avoid duplication
                        if (first < second)
                        {
                            neighbors.Add((first, second));
                        }
                        else
                        {
                            neighbors.Add((second, first));
                        }
                    }
                }
            }

            for (var y = 0; y < areas.Size.Y; y++)
            {
                for (var x = 1; x < areas.Size.X; x++)
                {
                    if (areas[x - 1, y] != 0 && areas[x, y] != 0 && areas[x - 1, y] != areas[x, y])
                    {
                        var first = areas[x - 1, y];
                        var second = areas[x, y];

                        // Consistently put the lesser number first to avoid duplication
                        if (first < second)
                        {
                            neighbors.Add((first, second));
                        }
                        else
                        {
                            neighbors.Add((second, first));
                        }
                    }
                }
            }

            return neighbors;
        }

        private void AddAdjacentLocations(Map map, Location location, HashSet<Location> locations, MapArray<byte> areas)
        {
            foreach (var adjacentLocation in AdjacentLocations(map, location))
            {
                if (areas[adjacentLocation] == 0 &&
                    (map.CanBuild(adjacentLocation) || map.CanTraverse(adjacentLocation)))
                {
                    locations.Add(adjacentLocation);
                }
            }
        }

        private IEnumerable<Location> AdjacentLocations(Map map, Location location)
        {
            var results = new List<Location>();

            var xVals = new List<int> { location.X - 1, location.X, location.X + 1 };
            xVals.Remove(-1);
            xVals.Remove(map.Size.X);

            var yVals = new List<int> { location.Y - 1, location.Y, location.Y + 1 };
            yVals.Remove(-1);
            yVals.Remove(map.Size.Y);

            foreach (var x in xVals)
            {
                foreach (var y in yVals)
                {
                    if (x != location.X || y != location.Y)
                    {
                        yield return new Location { X = x, Y = y };
                    }
                }
            }
        }
    }
}
