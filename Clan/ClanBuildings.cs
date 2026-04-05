using System;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NinjaHorizon.Function.Clan
{
    /// <summary>
    /// Handles clan building upgrades and management
    /// </summary>
    public static class ClanBuildingManager
    {
        /// <summary>
        /// Validates if a building can be upgraded
        /// </summary>
        public static bool CanUpgradeBuilding(int currentLevel, int clanGold, out int upgradeCost, out string errorMessage)
        {
            errorMessage = null;
            upgradeCost = ClanStatistics.CalculateBuildingUpgradeCost(currentLevel);

            if (currentLevel >= ClanConstants.MAX_BUILDING_LEVEL)
            {
                errorMessage = $"Building is already at maximum level ({ClanConstants.MAX_BUILDING_LEVEL})";
                return false;
            }

            if (clanGold < upgradeCost)
            {
                errorMessage = $"Not enough gold. Required: {upgradeCost}, Available: {clanGold}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Upgrades a specific building and returns the new buildings object
        /// </summary>
        public static ClanBuildings UpgradeBuilding(ClanBuildings buildings, BuildingType buildingType)
        {
            var updatedBuildings = new ClanBuildings
            {
                BathHouseLevel = buildings.BathHouseLevel,
                TeaHouseLevel = buildings.TeaHouseLevel,
                TrainingCentreLevel = buildings.TrainingCentreLevel
            };

            switch (buildingType)
            {
                case BuildingType.BathHouse:
                    updatedBuildings.BathHouseLevel++;
                    break;
                case BuildingType.TeaHouse:
                    updatedBuildings.TeaHouseLevel++;
                    break;
                case BuildingType.TrainingCentre:
                    updatedBuildings.TrainingCentreLevel++;
                    break;
                default:
                    throw new ArgumentException($"Unknown building type: {buildingType}");
            }

            return updatedBuildings;
        }

        /// <summary>
        /// Gets the current level of a specific building
        /// </summary>
        public static int GetBuildingLevel(ClanBuildings buildings, BuildingType buildingType)
        {
            return buildingType switch
            {
                BuildingType.BathHouse => buildings.BathHouseLevel,
                BuildingType.TeaHouse => buildings.TeaHouseLevel,
                BuildingType.TrainingCentre => buildings.TrainingCentreLevel,
                _ => throw new ArgumentException($"Unknown building type: {buildingType}")
            };
        }

        /// <summary>
        /// Gets building name for display purposes
        /// </summary>
        public static string GetBuildingName(BuildingType buildingType)
        {
            return buildingType switch
            {
                BuildingType.BathHouse => "Bath House",
                BuildingType.TeaHouse => "Tea House",
                BuildingType.TrainingCentre => "Training Centre",
                _ => "Unknown Building"
            };
        }

        /// <summary>
        /// Gets building effect description
        /// </summary>
        public static string GetBuildingEffect(BuildingType buildingType, int level)
        {
            return buildingType switch
            {
                BuildingType.BathHouse => 
                    $"Increases clan members' max stamina by {(level - 1) * ClanConstants.BATHHOUSE_STAMINA_PER_LEVEL} (Level {level})",
                BuildingType.TeaHouse => 
                    $"Reduces {ClanStatistics.CalculateStaminaReductionPerPlayer(level)} stamina per player when attacking (Level {level})",
                BuildingType.TrainingCentre => 
                    $"Affects {ClanStatistics.CalculatePlayersAffectedByAttack(level)} players per attack (Level {level})",
                _ => "Unknown effect"
            };
        }

        /// <summary>
        /// Creates default buildings for a new clan
        /// </summary>
        public static ClanBuildings CreateDefaultBuildings()
        {
            return new ClanBuildings
            {
                BathHouseLevel = 1,
                TeaHouseLevel = 1,
                TrainingCentreLevel = 1
            };
        }

        /// <summary>
        /// Parses buildings from JSON stored in PlayFab
        /// </summary>
        public static ClanBuildings ParseBuildings(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                    return CreateDefaultBuildings();

                return JsonConvert.DeserializeObject<ClanBuildings>(json);
            }
            catch
            {
                return CreateDefaultBuildings();
            }
        }

        /// <summary>
        /// Serializes buildings to JSON for PlayFab storage
        /// </summary>
        public static string SerializeBuildings(ClanBuildings buildings)
        {
            return JsonConvert.SerializeObject(buildings);
        }
    }
}

