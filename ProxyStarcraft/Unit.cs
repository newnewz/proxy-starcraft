﻿using ProxyStarcraft.Commands;
using ProxyStarcraft.Proto;
using System.Linq;

namespace ProxyStarcraft
{
    public abstract class Unit
    {
        protected readonly Translator translator;

        public Unit(Proto.Unit unit, Translator translator)
        {
            this.translator = translator;
            this.Raw = unit;
            this.RawType = translator.UnitTypes[unit.UnitType];
        }

        public Proto.Unit Raw { get; private set; }

        public UnitTypeData RawType { get; private set; }

        public abstract BuildingOrUnitType Type { get; }

        /// <summary>
        /// Determines if the unit is a mineral deposit.
        /// </summary>
        public bool IsMineralDeposit =>
            this.Raw.Alliance == Proto.Alliance.Neutral && this.RawType.HasMinerals; // TODO: Figure out if there is a valid case where this fails

        /// <summary>
        /// Determines if the unit is a vespene geyser.
        /// </summary>
        public bool IsVespeneGeyser =>
            this.Raw.Alliance == Proto.Alliance.Neutral && this.RawType.HasVespene; // TODO: Figure out if there is a valid case where this fails

        public bool IsBuildingSomething => translator.IsBuildingSomething(this.Raw);

        public bool HasBuff(BuffType buff) => translator.UnitHasBuff(this, buff);

        /// <summary>
        /// Sets a <seealso cref="BuffType"/> for a unit's game state (so that other computation within the same frame doesn't attempt to do something else with the unit)
        /// </summary>
        /// <param name="buff"></param>
        /// <remarks>This will technically add a bunch of buffs that an entity "can't" have, but are effectively all identical. 
        /// (e.g. "Banshee Cloak", "Ghost Cloak", "Dark Templar Cloak", and "Mothership Field Cloak")
        /// </remarks>
        public void SetBuff(BuffType buff) => translator.ApplyBuffType(this, buff);

        public ulong Tag => this.Raw.Tag;

        public float X => this.Raw.Pos.X;

        public float Y => this.Raw.Pos.Y;

        public bool IsBuilt => this.Raw.BuildProgress == 1.0f;

        public bool IsMainBase =>
            this.Type == TerranBuildingType.CommandCenter ||
            this.Type == TerranBuildingType.PlanetaryFortress ||
            this.Type == TerranBuildingType.OrbitalCommand ||
            this.Type == ProtossBuildingType.Nexus ||
            this.Type == ZergBuildingType.Hatchery ||
            this.Type == ZergBuildingType.Lair ||
            this.Type == ZergBuildingType.Hive;

        public bool IsVespeneBuilding =>
            this.Type == TerranBuildingType.Refinery||
            this.Type == ProtossBuildingType.Assimilator ||
            this.Type == ZergBuildingType.Extractor;

        public bool IsWorker =>
            this.Type == TerranUnitType.SCV ||
            this.Type == ProtossUnitType.Probe ||
            this.Type == ZergUnitType.Drone;

        public bool IsBuilding(BuildingOrUnitType buildingOrUnitType)
        {
            return translator.IsBuilding(this.Raw, buildingOrUnitType);
        }

        public bool IsCasting(SpecialAbilityType specialAbilityType) => translator.IsCasting(this, specialAbilityType);

        /// <summary>
        /// Determines if this unit is of the specified type, or possibly an upgraded form of the same thing.
        /// </summary>
        public bool CountsAs(BuildingOrUnitType type)
        {
            if (this.Type == type)
            {
                return true;
            }

            // Handle buildings that get upgraded to 'different types' but still count
            // TODO: Handle this better, elsewhere
            if (type == TerranBuildingType.CommandCenter &&
                (this.Type == TerranBuildingType.PlanetaryFortress ||
                 this.Type == TerranBuildingType.OrbitalCommand))
            {
                return true;
            }

            if (type == ZergBuildingType.Hatchery &&
                (this.Type == ZergBuildingType.Lair ||
                 this.Type == ZergBuildingType.Hive))
            {
                return true;
            }

            if (type == ZergBuildingType.Lair &&
                this.Type == ZergBuildingType.Hive)
            {
                return true;
            }

            if (type == ZergBuildingType.HydraliskDen &&
                this.Type == ZergBuildingType.LurkerDen)
            {
                return true;
            }

            if (type == ZergBuildingType.Spire &&
                this.Type == ZergBuildingType.GreaterSpire)
            {
                return true;
            }

            if (type == TerranBuildingType.TechLab &&
                (this.Type == TerranBuildingType.BarracksTechLab ||
                 this.Type == TerranBuildingType.FactoryTechLab ||
                 this.Type == TerranBuildingType.StarportTechLab))
            {
                return true;
            }

            if (type == TerranBuildingType.Reactor &&
                (this.Type == TerranBuildingType.BarracksReactor ||
                 this.Type == TerranBuildingType.FactoryReactor ||
                 this.Type == TerranBuildingType.StarportReactor))
            {
                return true;
            }

            return false;
        }

        public MoveCommand Move(float x, float y)
        {
            return new MoveCommand(this, x, y);
        }

        public MoveCommand Move(Location location)
        {
            var point = (Point2D)location;

            return new MoveCommand(this, point.X, point.Y);
        }

        public AttackCommand Attack(Unit target)
        {
            return new AttackCommand(this, target);
        }

        public AttackMoveCommand AttackMove(float x, float y)
        {
            return new AttackMoveCommand(this, x, y);
        }

        public AttackMoveCommand AttackMove(Location location)
        {
            var point = (Point2D)location;

            return new AttackMoveCommand(this, point.X, point.Y);
        }

        public HarvestCommand Harvest(Unit target)
        {
            return new HarvestCommand(this, target);
        }

        public BuildCommand Build(BuildingType buildingType, IBuildLocation location)
        {
            return new BuildCommand(this, buildingType, location);
        }
        
        public TrainCommand Train(UnitType unitType)
        {
            var ability = translator.GetBuildAction(unitType);
            return new TrainCommand(this, unitType);
        }
        
        public override string ToString() => this.Type.ToString();
    }
}
