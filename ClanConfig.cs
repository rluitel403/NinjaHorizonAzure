namespace NinjaHorizon.Function
{
    public static class ClanConfig
    {
        // Clan Size Limits
        public const int MAX_CLAN_MEMBERS = 50;
        public const int MAX_ATTACKING_MEMBERS = 25;

        // Stamina Configuration
        public const int MAX_STAMINA_PER_PLAYER = 100;
        public const int MAX_TOTAL_CLAN_STAMINA = MAX_CLAN_MEMBERS * MAX_STAMINA_PER_PLAYER; // 5000

        // Attack Configuration
        public const int ATTACKS_NEEDED_PER_MEMBER = 4;
        public const int TOTAL_ATTACKS_TO_WEAKEN = MAX_ATTACKING_MEMBERS * ATTACKS_NEEDED_PER_MEMBER; // 100

        // Attack Success Chances
        public const double BASE_ATTACK_SUCCESS_CHANCE = 0.8; // 80%
        public const double MIN_ATTACK_SUCCESS_CHANCE = 0.1;  // 10%

        // Stamina Reduction Per Attack
        // Formula: To fully weaken a clan (5000 stamina) with 100 attacks
        // Each attack should reduce: 5000 / 100 = 50 stamina
        public const int STAMINA_REDUCTION_PER_ATTACK = MAX_TOTAL_CLAN_STAMINA / TOTAL_ATTACKS_TO_WEAKEN; // 50

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
    }
}