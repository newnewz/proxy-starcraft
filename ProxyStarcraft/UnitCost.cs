﻿using System.Linq;

namespace ProxyStarcraft
{
    /// <summary>
    /// Represents everything needed to create a unit or structure, including prerequisites.
    /// </summary>
    public class UnitCost
    {
        public UnitCost(uint minerals, uint vespene, float supply, BuildingOrUnitType builder, BuildingOrUnitType prerequisite)
        {
            this.Minerals = minerals;
            this.Vespene = vespene;
            this.Supply = supply;
            this.Builder = builder;
            this.Prerequisite = prerequisite;
        }

        public uint Minerals { get; private set; }

        public uint Vespene { get; private set; }

        public float Supply { get; private set; }

        public BuildingOrUnitType Builder { get; private set; }

        public BuildingOrUnitType Prerequisite { get; private set; }

        /// <summary>
        /// Determines whether the cost/requirements are currently met and therefore the unit in question can be built.
        /// </summary>
        public bool IsMet(GameState gameState)
        {
            if (!HasResources(gameState))
            {
                return false;
            }

            if (!HasPrerequisite(gameState))
            {
                return false;
            }
            
            if (GetBuilder(gameState) == null)
            {
                return false;
            }

            return true;
        }

        public bool HasResources(GameState gameState)
        {
            if (gameState.Response.Observation.PlayerCommon.Minerals < this.Minerals)
            {
                return false;
            }

            if (gameState.Response.Observation.PlayerCommon.Vespene < this.Vespene)
            {
                return false;
            }

            if (gameState.Response.Observation.PlayerCommon.FoodCap < gameState.Response.Observation.PlayerCommon.FoodUsed + this.Supply)
            {
                return false;
            }

            return true;
        }

        public bool HasPrerequisite(GameState gameState)
        {
            if (this.Prerequisite != null && !gameState.Units.Any(u => IsPrerequisiteType(u) && u.IsBuilt))
            {
                // This is handled separately in the GetBuilder(...) logic.
                // TODO: Find cleaner way to do this.
                if (this.Prerequisite != TerranBuildingType.TechLab &&
                    this.Prerequisite != TerranBuildingType.BarracksTechLab &&
                    this.Prerequisite != TerranBuildingType.FactoryTechLab &&
                    this.Prerequisite != TerranBuildingType.StarportTechLab)
                {
                    return false;
                }
            }

            return true;
        }

        public Unit GetBuilder(GameState gameState)
        {
            if (this.Prerequisite == TerranBuildingType.TechLab ||
                this.Prerequisite == TerranBuildingType.BarracksTechLab ||
                this.Prerequisite == TerranBuildingType.FactoryTechLab ||
                this.Prerequisite == TerranBuildingType.StarportTechLab)
            {
                var builder = gameState.Units.FirstOrDefault(
                    u =>
                    u.CountsAs(this.Builder) && !u.IsBuildingSomething && u.IsBuilt &&
                    u.Raw.AddOnTag != 0 &&
                    gameState.Units.Single(a => a.Tag == u.Raw.AddOnTag).CountsAs(TerranBuildingType.TechLab));
                return builder;
            }
            
            return gameState.Units.FirstOrDefault(u => u.CountsAs(this.Builder) && !u.IsBuildingSomething && u.IsBuilt);
        }

        private bool IsBuilderType(Unit unit)
        {
            return unit.CountsAs(this.Builder);
        }

        private bool IsPrerequisiteType(Unit unit)
        {
            return unit.CountsAs(this.Prerequisite);
        }
    }
}
