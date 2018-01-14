﻿using System;
using System.Collections.Generic;
using System.Linq;
using ProxyStarcraft.Maps;

namespace ProxyStarcraft.Basic
{
    /// <summary>
    /// A simple strategy for where to put buildings. Relies on BasicMapAnalyzer.
    /// </summary>
    public class BasicProductionStrategy : IProductionStrategy
    {
        private IExpansionStrategy expansionStrategy;

        public BasicProductionStrategy(IExpansionStrategy expansionStrategy)
        {
            this.expansionStrategy = expansionStrategy;
        }

        public IBuildLocation GetPlacement(BuildingType building, GameState gameState)
        {
            // Obviously different rules apply to Vespene Geysers
            if (building == TerranBuildingType.Refinery ||
                building == ProtossBuildingType.Assimilator ||
                building == ZergBuildingType.Extractor)
            {
                return GetVespeneBuildingPlacement(gameState);
            }

            // Building upgrades happen in place
            if (building == TerranBuildingType.PlanetaryFortress ||
                building == TerranBuildingType.OrbitalCommand ||
                building == ZergBuildingType.Lair ||
                building == ZergBuildingType.Hive ||
                building == ZergBuildingType.LurkerDen ||
                building == ZergBuildingType.GreaterSpire)
            {
                return new UpgradeBuildLocation();
            }

            if (building == TerranBuildingType.BarracksReactor ||
                building == TerranBuildingType.BarracksTechLab ||
                building == TerranBuildingType.FactoryReactor ||
                building == TerranBuildingType.FactoryTechLab ||
                building == TerranBuildingType.StarportReactor ||
                building == TerranBuildingType.StarportTechLab)
            {
                // TODO: Make sure this is possible - will probably need to pass in the parent building or something
                return new AddOnBuildLocation();
            }

            

            // Note: Extractor and Hatchery already accounted for, above
            var requireCreep = building.Value is ZergBuilding;

            // We're going to make some dumb assumptions here:
            // 1. We'd like to build this building very near where a main base currently is
            // 2. As long as we don't block anything, it doesn't matter where it goes
            //
            // Exception for main bases (see below)
            var startingLocations = GetMainBaseLocations(gameState);

            var size = gameState.Translator.GetBuildingSize(building);

            var hasAddOn = building == TerranBuildingType.Barracks ||
                           building == TerranBuildingType.Factory ||
                           building == TerranBuildingType.Starport;

            var includeResourcePadding = false;

            if (building == TerranBuildingType.CommandCenter ||
                building == ProtossBuildingType.Nexus ||
                building == ZergBuildingType.Hatchery)
            {
                includeResourcePadding = true;
                var deposit = expansionStrategy.GetNextExpansion(gameState);
                startingLocations = new List<Location> { deposit.Center };
            }

            var buildLocation = gameState.Map.BreadthFirstSearch(
                (map, loc) => map.CanBuild(size, loc, requireCreep, hasAddOn, includeResourcePadding),
                startingLocations,
                (map, loc) => map.CanTraverse(loc));

            if (buildLocation.HasValue)
            {
                return new StandardBuildLocation(buildLocation.Value);
            }

            throw new InvalidOperationException("Cannot find placement location anywhere on map.");
        }

        public Command Produce(BuildingOrUnitType buildingOrUnit, GameState gameState)
        {
            var cost = gameState.Translator.GetCost(buildingOrUnit);
            var builder = cost.GetBuilder(gameState);

            gameState.Observation.PlayerCommon.Minerals -= cost.Minerals;
            gameState.Observation.PlayerCommon.Vespene -= cost.Vespene;
            gameState.Observation.PlayerCommon.FoodUsed += (uint)Math.Ceiling(cost.Supply);

            if (buildingOrUnit.IsBuildingType)
            {
                var buildingType = (BuildingType)buildingOrUnit;
                var location = this.GetPlacement(buildingType, gameState);
                return builder.Build(buildingType, location);
            }
            else
            {
                return builder.Train((UnitType)buildingOrUnit);
            }
        }
        
        private VespeneBuildLocation GetVespeneBuildingPlacement(GameState gameState)
        {
            // TODO: Just add main bases to GameState? I'm using them enough.
            var bases = gameState.Units.Where(u => u.IsMainBase).Cast<Building>().ToList();
            var vespeneBuildings = gameState.Units.Where(u => u.IsVespeneBuilding).ToList();
            var deposits = gameState.GetMapData<BasicMapData>().GetControlledDeposits(bases);

            // I don't know if Vespene Geysers still exist on the map once there's a building on them.

            // Better to build where there's a finished base than an in-progress one.
            var idealLocation = GetVespeneBuildingPlacement(deposits, bases, vespeneBuildings, true) ?? GetVespeneBuildingPlacement(deposits, bases, vespeneBuildings, false);
            if (idealLocation != null)
            {
                return idealLocation;
            }

            throw new InvalidOperationException();
        }

        private VespeneBuildLocation GetVespeneBuildingPlacement(IReadOnlyList<Deposit> deposits, IReadOnlyList<Building> bases, IReadOnlyList<Unit> vespeneBuildings, bool baseMustBeBuilt)
        {
            // Better to build where there's a finished base than an in-progress one.
            foreach (var deposit in deposits)
            {
                var closestBase = bases.Single(b => b.GetDistance(deposit.Center) < 10f);
                if (closestBase.IsBuilt == baseMustBeBuilt)
                {
                    var vespeneGeyser = deposit.Resources.Where(
                        u => u.IsVespeneGeyser && !vespeneBuildings.Any(vb => vb.GetDistance(u) < 1f)).FirstOrDefault();

                    if (vespeneGeyser != null)
                    {
                        return new VespeneBuildLocation(vespeneGeyser);
                    }
                }
            }

            return null;
        }

        private List<Location> GetMainBaseLocations(GameState gameState)
        {
            var results = new List<Location>();

            foreach (var unit in gameState.Units)
            {
                if (unit.Type == TerranBuildingType.CommandCenter ||
                    unit.Type == TerranBuildingType.OrbitalCommand ||
                    unit.Type == TerranBuildingType.PlanetaryFortress ||
                    unit.Type == ProtossBuildingType.Nexus ||
                    unit.Type == ZergBuildingType.Hatchery ||
                    unit.Type == ZergBuildingType.Lair ||
                    unit.Type == ZergBuildingType.Hive)
                {
                    // They're all 5x5 so the center is 2 spaces up and to the right of (X, Y) as we're marking it
                    results.Add(new Location { X = (int)unit.X + 2, Y = (int)unit.Y + 2 });
                }
            }

            return results;
        }
    }
}
