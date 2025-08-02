using System;

namespace NinjaHorizon.Function
{
    public static class ClanConfig
    {
        // Clan Size Limits
        public const int MAX_CLAN_MEMBERS = 50;
        public const int MAX_ATTACKING_MEMBERS = 25;

        // Stamina Configuration
        public const int BASE_MAX_STAMINA_PER_PLAYER = 100;
        public const int MAX_TOTAL_CLAN_STAMINA = MAX_CLAN_MEMBERS * BASE_MAX_STAMINA_PER_PLAYER; // 5000

        // Attack Configuration
        public const int ATTACKS_NEEDED_PER_MEMBER = 4;
        public const int TOTAL_ATTACKS_TO_WEAKEN = MAX_ATTACKING_MEMBERS * ATTACKS_NEEDED_PER_MEMBER; // 100

        // Attack Success Chances
        public const double BASE_ATTACK_SUCCESS_CHANCE = 0.6; // 60% base
        public const double MIN_ATTACK_SUCCESS_CHANCE = 0.1;  // 10%
        public const double MAX_ATTACK_SUCCESS_CHANCE = 0.9;  // 90%

        // Base Stamina Reduction Per Attack
        public const int BASE_STAMINA_REDUCTION_PER_ATTACK = 50;

        // Reputation Configuration
        public const int MIN_REPUTATION_REWARD = 3;
        public const int MAX_REPUTATION_REWARD = 100;
        public const int REPUTATION_PER_RANK_DIFFERENCE = 5;
        public const int REPUTATION_PER_1000_POINTS = 1000; // 1000 reputation = 1 rank

        // Manual Stamina Restoration
        public const int MANUAL_RESTORE_TOKEN_COST = 20;
        public const int MANUAL_RESTORE_STAMINA_AMOUNT = 50;

        // Automatic Stamina Restoration
        public const int AUTO_RESTORE_INTERVAL_MINUTES = 30;
        public const int AUTO_RESTORE_STAMINA_AMOUNT = 50;

        // Attack Window System
        public const int ATTACK_WINDOW_MINUTES = 30; // 30-minute windows
                                                     // No cooldown - players can attack continuously

        // Dynamic Attack Count Weakening System
        // BALANCE: Larger clans are harder to weaken, smaller clans are easier
        public const int ATTACKS_PER_MEMBER_TO_WEAKEN = 4; // Attacks needed per clan member to fully weaken
        public const int MAX_ATTACKING_MEMBERS_PER_CLAN = 25; // Max members that can attack from one clan (configurable)
        public const int ATTACKS_PER_MEMBER_PER_MINUTE = 6; // Max attack rate per member

        /* BALANCE EXAMPLES:
         * 10-member clan: needs 40 attacks to weaken (2.5% per attack)
         * 25-member clan: needs 100 attacks to weaken (1% per attack)  
         * 50-member clan: needs 200 attacks to weaken (0.5% per attack)
         * 
         * WORST CASE SCENARIO:
         * 10 attacking clans × 25 members × 6 attacks/min = 1,500 attacks/min
         * In 30-minute window = 45,000 total attacks possible
         * Even a 50-member clan (needs 200 attacks) can be overwhelmed 225 times over
         * But this requires perfect coordination from 250 attackers!
         */

        // Calculate attacks needed to fully weaken a clan: Member Count × ATTACKS_PER_MEMBER_TO_WEAKEN
        // Examples: 25 members = 100 attacks, 50 members = 200 attacks, 10 members = 40 attacks
        public static int GetAttacksNeededToWeaken(int clanMemberCount)
        {
            return clanMemberCount * ATTACKS_PER_MEMBER_TO_WEAKEN;
        }

        public static double GetAttackWeakeningRatio(int clanMemberCount)
        {
            return 1.0 / GetAttacksNeededToWeaken(clanMemberCount);
        }

        // Helper methods for attack capacity calculations
        public static int GetMaxAttacksPerMinuteFromClan()
        {
            return MAX_ATTACKING_MEMBERS_PER_CLAN * ATTACKS_PER_MEMBER_PER_MINUTE; // 25 × 6 = 150 attacks/minute
        }

        public static int GetMaxAttacksPerWindowFromClan()
        {
            return GetMaxAttacksPerMinuteFromClan() * ATTACK_WINDOW_MINUTES; // 150 × 30 = 4,500 attacks per 30-min window
        }

        public static int GetWorstCaseAttacksPerWindow(int attackingClans = 10)
        {
            return GetMaxAttacksPerWindowFromClan() * attackingClans; // Up to 45,000 attacks from 10 clans
        }

        // Building Bonuses
        // TeaHouse - Increases attack success chance
        public const double TEAHOUSE_ATTACK_BONUS_PER_LEVEL = 0.03; // +3% per level (max +12% at level 5)

        // BathHouse - Increases stamina capacity
        public const int BATHHOUSE_STAMINA_BONUS_PER_LEVEL = 20; // +20 stamina per level (max +80 at level 5)

        // TrainingCentre - Increases attack damage/weakening power
        public const double TRAINING_CENTRE_DAMAGE_MULTIPLIER_PER_LEVEL = 0.15; // +15% damage per level (max +60% at level 5)

        // Building Upgrade Costs (example - you can adjust these)
        public static readonly int[] BUILDING_UPGRADE_COSTS = { 0, 100, 250, 500, 1000, 2000 }; // Index = level, Value = cost to upgrade to that level

        // Time Window Calculation
        public static long GetCurrentTimeWindow()
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var minutesSinceEpoch = (long)(DateTime.UtcNow - epoch).TotalMinutes;
            return minutesSinceEpoch / ATTACK_WINDOW_MINUTES;
        }

        public static DateTime GetWindowStartTime(long timeWindow)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMinutes(timeWindow * ATTACK_WINDOW_MINUTES);
        }

        public static DateTime GetWindowEndTime(long timeWindow)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMinutes((timeWindow + 1) * ATTACK_WINDOW_MINUTES);
        }

        // Building Bonus Calculations
        public static double GetTeaHouseAttackBonus(int level)
        {
            return Math.Min(level * TEAHOUSE_ATTACK_BONUS_PER_LEVEL, 4 * TEAHOUSE_ATTACK_BONUS_PER_LEVEL); // Max level 5, but bonus caps at level 4
        }

        public static int GetBathHouseStaminaBonus(int level)
        {
            return Math.Min(level * BATHHOUSE_STAMINA_BONUS_PER_LEVEL, 4 * BATHHOUSE_STAMINA_BONUS_PER_LEVEL); // Max level 5, but bonus caps at level 4
        }

        public static double GetTrainingCentreDamageMultiplier(int level)
        {
            return 1.0 + Math.Min(level * TRAINING_CENTRE_DAMAGE_MULTIPLIER_PER_LEVEL, 4 * TRAINING_CENTRE_DAMAGE_MULTIPLIER_PER_LEVEL); // Max level 5, but bonus caps at level 4
        }

        public static int GetMaxStaminaForPlayer(int bathHouseLevel)
        {
            return BASE_MAX_STAMINA_PER_PLAYER + GetBathHouseStaminaBonus(bathHouseLevel);
        }
    }
}