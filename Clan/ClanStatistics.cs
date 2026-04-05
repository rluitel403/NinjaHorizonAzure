using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PlayFab;

namespace NinjaHorizon.Function.Clan
{
    /// <summary>
    /// Handles all stamina calculations, bleeding mechanics, and reputation rewards
    /// </summary>
    public static class ClanStatistics
    {
        /// <summary>
        /// Calculates current stamina based on last update time and regeneration
        /// Caps at effective max (stored max + building bonus)
        /// </summary>
        public static int CalculateCurrentStamina(int storedStamina, long lastUpdateTimestamp, int storedMaxStamina, int bathHouseLevel)
        {
            long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long secondsElapsed = currentTimestamp - lastUpdateTimestamp;
            
            double regenAmount = secondsElapsed * ClanConstants.STAMINA_REGEN_PER_SECOND;
            
            // Calculate effective max (stored max + building bonus)
            int buildingBonus = (bathHouseLevel - 1) * ClanConstants.BATHHOUSE_STAMINA_PER_LEVEL;
            int effectiveMaxStamina = storedMaxStamina + buildingBonus;
            
            // Current stamina can regenerate up to effective max
            int currentStamina = (int)Math.Min(effectiveMaxStamina, storedStamina + regenAmount);
            
            return currentStamina;
        }

        /// <summary>
        /// Calculates the effective max stamina (base + personal upgrades + building bonus)
        /// Building bonus is applied dynamically, not stored
        /// </summary>
        public static int CalculateEffectiveMaxStamina(int storedMaxStamina, int clanBathHouseLevel)
        {
            int buildingBonus = (clanBathHouseLevel - 1) * ClanConstants.BATHHOUSE_STAMINA_PER_LEVEL;
            return storedMaxStamina + buildingBonus;
        }

        /// <summary>
        /// Calculates total stamina for all clan members
        /// </summary>
        public static (int totalStamina, int maxPossibleStamina) CalculateClanStamina(
            List<ClanMemberInfo> members)
        {
            int totalStamina = members.Sum(m => m.CurrentStamina);
            int maxPossibleStamina = members.Sum(m => m.MaxStamina);
            
            return (totalStamina, maxPossibleStamina);
        }

        /// <summary>
        /// Calculates bleeding percentage (0.0 to 1.0)
        /// </summary>
        public static double CalculateBleedingPercentage(int totalStamina, int maxPossibleStamina)
        {
            if (maxPossibleStamina == 0) return 0.0;
            return (double)totalStamina / maxPossibleStamina;
        }

        /// <summary>
        /// Checks if a clan is bleeding (stamina below threshold)
        /// </summary>
        public static bool IsClanBleeding(double bleedingPercentage)
        {
            return bleedingPercentage <= ClanConstants.BLEEDING_THRESHOLD;
        }

        /// <summary>
        /// Calculates reputation reward for attacking a bleeding clan
        /// Formula: Base reputation + scaled by rank difference
        /// </summary>
        public static int CalculateReputationReward(
            int attackerClanReputation, 
            int targetClanReputation, 
            bool targetIsBleeding)
        {
            if (!targetIsBleeding)
                return 0;

            int reputationDifference = targetClanReputation - attackerClanReputation;
            
            // If target is below attacker's rank
            if (reputationDifference <= 0)
            {
                return ClanConstants.BASE_REPUTATION_REWARD;
            }
            
            // If target is above attacker's rank - scale the reward
            // For every 100 reputation difference, add 1 point (minimum 5, maximum 50)
            int scaledReward = ClanConstants.HIGH_RANK_REPUTATION_REWARD + 
                              (int)(reputationDifference * ClanConstants.REPUTATION_SCALING_FACTOR);
            
            return Math.Min(50, Math.Max(ClanConstants.HIGH_RANK_REPUTATION_REWARD, scaledReward));
        }

        /// <summary>
        /// Calculates how much stamina is reduced per player when attacked
        /// Based on attacker's Tea House level
        /// </summary>
        public static int CalculateStaminaReductionPerPlayer(int attackerTeaHouseLevel)
        {
            return ClanConstants.TEAHOUSE_BASE_STAMINA_REDUCTION + 
                   (attackerTeaHouseLevel - 1) * ClanConstants.TEAHOUSE_STAMINA_REDUCTION_PER_LEVEL;
        }

        /// <summary>
        /// Calculates how many players are affected by an attack
        /// Based on attacker's Training Centre level
        /// </summary>
        public static int CalculatePlayersAffectedByAttack(int attackerTrainingCentreLevel)
        {
            return ClanConstants.TRAINING_CENTRE_BASE_PLAYERS_HIT + 
                   (attackerTrainingCentreLevel - 1) * ClanConstants.TRAINING_CENTRE_PLAYERS_PER_LEVEL;
        }

        /// <summary>
        /// Selects random players from target clan to be affected by attack
        /// </summary>
        public static List<ClanMemberInfo> SelectRandomPlayersForAttack(
            List<ClanMemberInfo> targetMembers, 
            int numberOfPlayers)
        {
            var random = new Random();
            int actualCount = Math.Min(numberOfPlayers, targetMembers.Count);
            
            return targetMembers
                .OrderBy(x => random.Next())
                .Take(actualCount)
                .ToList();
        }

        /// <summary>
        /// Calculates building upgrade cost
        /// </summary>
        public static int CalculateBuildingUpgradeCost(int currentLevel)
        {
            return (int)(ClanConstants.BUILDING_UPGRADE_BASE_COST * 
                         Math.Pow(ClanConstants.BUILDING_UPGRADE_COST_MULTIPLIER, currentLevel - 1));
        }

        /// <summary>
        /// Validates if a player has enough stamina for an action
        /// </summary>
        public static bool HasEnoughStamina(int currentStamina, int requiredStamina)
        {
            return currentStamina >= requiredStamina;
        }

        /// <summary>
        /// Creates updated stamina data after spending stamina
        /// </summary>
        public static (int newStamina, long newTimestamp) SpendStamina(
            int currentStamina, 
            int amount)
        {
            int newStamina = Math.Max(0, currentStamina - amount);
            long newTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            return (newStamina, newTimestamp);
        }

        /// <summary>
        /// Parses player stamina data from PlayFab statistics
        /// </summary>
        public static PlayerStaminaData ParsePlayerStamina(Dictionary<string, int> stats)
        {
            return new PlayerStaminaData
            {
                Stamina = stats.ContainsKey(ClanConstants.STAT_STAMINA) 
                    ? stats[ClanConstants.STAT_STAMINA] : ClanConstants.BASE_MAX_STAMINA,
                MaxStamina = stats.ContainsKey(ClanConstants.STAT_MAX_STAMINA) 
                    ? stats[ClanConstants.STAT_MAX_STAMINA] : ClanConstants.BASE_MAX_STAMINA,
                StaminaLastUpdate = stats.ContainsKey(ClanConstants.STAT_STAMINA_LAST_UPDATE) 
                    ? stats[ClanConstants.STAT_STAMINA_LAST_UPDATE] : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Reputation = stats.ContainsKey(ClanConstants.STAT_REPUTATION) 
                    ? stats[ClanConstants.STAT_REPUTATION] : 0
            };
        }

        /// <summary>
        /// Creates a ClanStatus object from member data
        /// </summary>
        public static ClanStatus CreateClanStatus(List<ClanMemberInfo> members)
        {
            var (totalStamina, maxPossibleStamina) = CalculateClanStamina(members);
            double bleedingPercentage = CalculateBleedingPercentage(totalStamina, maxPossibleStamina);
            bool isBleeding = IsClanBleeding(bleedingPercentage);

            return new ClanStatus
            {
                TotalStamina = totalStamina,
                MaxPossibleStamina = maxPossibleStamina,
                BleedingPercentage = bleedingPercentage,
                IsBleeding = isBleeding
            };
        }
    }
}

