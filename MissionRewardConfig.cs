using System;

namespace NinjaHorizon.Function
{
    /// <summary>
    /// Configuration constants for mission rewards and scaling
    /// </summary>
    public static class MissionRewardConfig
    {
        // Difficulty Scaling
        public const int DIFFICULTY_SCALE_MULTIPLIER = 10;
        
        // Village Mission Rewards (PVE)
        public const float DIFFICULTY_0_REWARD_MULTIPLIER = 1.0f;
        public const float DIFFICULTY_1_REWARD_MULTIPLIER = 1.2f;
        public const float DIFFICULTY_2_REWARD_MULTIPLIER = 1.4f;
        
        public const int DIFFICULTY_0_DROP_CHANCE = 1;
        public const int DIFFICULTY_1_DROP_CHANCE = 2;
        public const int DIFFICULTY_2_DROP_CHANCE = 4;
        
        public const float DIFFICULTY_0_CHANCE_SCALER = 1.0f;
        public const float DIFFICULTY_1_CHANCE_SCALER = 1.5f;
        public const float DIFFICULTY_2_CHANCE_SCALER = 2.0f;
        
        // Tower Rewards
        public const int TOWER_GOLD_MULTIPLIER = 100;
        public const int TOWER_XP_MULTIPLIER = 100;
        public const int TOWER_TOKEN_FREQUENCY = 5; // Every 5 floors
        public const int TOWER_TOKEN_AMOUNT = 5;
        
        // Hunting House Rewards
        public const float HUNTING_HOUSE_BASE_XP = 400f;
        public const float HUNTING_HOUSE_BASE_GOLD = 400f;
        public const int HUNTING_HOUSE_BASE_DROP_CHANCE = 30;
        public const int HUNTING_HOUSE_DROP_CHANCE_PER_FLOOR = 7;
        public const float HUNTING_HOUSE_CHANCE_SCALER_DIVISOR = 9f;
        
        // Enemy Tier by Difficulty
        public const int DIFFICULTY_0_ENEMY_TIER = 3; // 4-star
        public const int DIFFICULTY_1_ENEMY_TIER = 4; // 5-star
        public const int DIFFICULTY_2_ENEMY_TIER = 5; // Max tier
        
        // Energy Costs
        public const int VILLAGE_DIFFICULTY_0_ENERGY = 3;
        public const int VILLAGE_DIFFICULTY_1_ENERGY = 4;
        public const int VILLAGE_DIFFICULTY_2_ENERGY = 5;
        public const int TOWER_ENERGY = 5;
        public const int HUNTING_HOUSE_FLOOR_0_2_ENERGY = 5;
        public const int HUNTING_HOUSE_FLOOR_3_5_ENERGY = 6;
        public const int HUNTING_HOUSE_FLOOR_6_7_ENERGY = 7;
        public const int HUNTING_HOUSE_FLOOR_8_PLUS_ENERGY = 8;
        
        // Random Drop Chances
        public const int ENERGY_DROP_CHANCE = 30;
        public const int ENERGY_DROP_AMOUNT = 1;
        
        /// <summary>
        /// Calculate reward multiplier based on difficulty
        /// </summary>
        public static float GetRewardMultiplier(int difficulty)
        {
            return difficulty switch
            {
                0 => DIFFICULTY_0_REWARD_MULTIPLIER,
                1 => DIFFICULTY_1_REWARD_MULTIPLIER,
                _ => DIFFICULTY_2_REWARD_MULTIPLIER
            };
        }
        
        /// <summary>
        /// Calculate drop chance boost based on difficulty
        /// </summary>
        public static int GetDropChanceBoost(int difficulty)
        {
            return difficulty switch
            {
                0 => DIFFICULTY_0_DROP_CHANCE,
                1 => DIFFICULTY_1_DROP_CHANCE,
                _ => DIFFICULTY_2_DROP_CHANCE
            };
        }
        
        /// <summary>
        /// Calculate chance scaler based on difficulty
        /// </summary>
        public static float GetChanceScaler(int difficulty)
        {
            return difficulty switch
            {
                0 => DIFFICULTY_0_CHANCE_SCALER,
                1 => DIFFICULTY_1_CHANCE_SCALER,
                _ => DIFFICULTY_2_CHANCE_SCALER
            };
        }
        
        /// <summary>
        /// Get enemy tier based on difficulty
        /// </summary>
        public static int GetEnemyTier(int difficulty)
        {
            return difficulty switch
            {
                0 => DIFFICULTY_0_ENEMY_TIER,
                1 => DIFFICULTY_1_ENEMY_TIER,
                _ => DIFFICULTY_2_ENEMY_TIER
            };
        }
        
        /// <summary>
        /// Calculate hunting house reward scaler based on floor
        /// </summary>
        public static float GetHuntingHouseRewardScaler(int floorId)
        {
            return 1f + (floorId / 2f);
        }
        
        /// <summary>
        /// Calculate hunting house drop chance based on floor
        /// </summary>
        public static int GetHuntingHouseDropChance(int floorId)
        {
            return HUNTING_HOUSE_BASE_DROP_CHANCE + ((floorId + 1) * HUNTING_HOUSE_DROP_CHANCE_PER_FLOOR);
        }
        
        /// <summary>
        /// Calculate hunting house chance scaler based on floor
        /// </summary>
        public static float GetHuntingHouseChanceScaler(int floorId)
        {
            return 1f + (floorId / HUNTING_HOUSE_CHANCE_SCALER_DIVISOR);
        }
    }
}

