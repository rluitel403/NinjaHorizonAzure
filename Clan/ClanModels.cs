using System;
using System.Collections.Generic;

namespace NinjaHorizon.Function.Clan
{
    // Request/Response Models
    public class CreateClanRequest
    {
        public string ClanName { get; set; }
    }

    public class CreateClanResponse
    {
        public string ClanId { get; set; }
        public string ClanName { get; set; }
    }

    public class JoinClanRequest
    {
        public string ClanId { get; set; }
    }

    public class ApplyToClanRequest
    {
        public string ClanId { get; set; }
    }

    public class InviteToClanRequest
    {
        public string ClanId { get; set; }
        public string TargetPlayerId { get; set; }
    }

    public class AcceptApplicationRequest
    {
        public string ClanId { get; set; }
        public string ApplicantEntityId { get; set; }
    }

    public class RejectApplicationRequest
    {
        public string ClanId { get; set; }
        public string ApplicantEntityId { get; set; }
    }

    public class UpgradeBuildingRequest
    {
        public string ClanId { get; set; }
        public BuildingType BuildingType { get; set; }
    }

    public class AttackClanRequest
    {
        public string TargetClanId { get; set; }
    }

    public class AttackClanResponse
    {
        public int StaminaSpent { get; set; }
        public int ReputationGained { get; set; }
        public bool TargetIsBleeding { get; set; }
        public double TargetBleedingPercentage { get; set; }
        public List<string> AffectedPlayers { get; set; }
    }

    public class ContributeReputationRequest
    {
        public string ClanId { get; set; }
        public int Amount { get; set; }
    }

    public class GetClanDetailsRequest
    {
        public string ClanId { get; set; }
    }

    public class SearchClanRequest
    {
        public string SearchQuery { get; set; }
    }

    public class GetClanDetailsResponse
    {
        public string ClanId { get; set; }
        public string ClanName { get; set; }
        public string ShortCode { get; set; }
        public int TotalReputation { get; set; }
        public int ClanGold { get; set; }
        public ClanBuildings Buildings { get; set; }
        public List<ClanMemberInfo> Members { get; set; }
        public ClanStatus Status { get; set; }
    }

    public class GetClanLeaderboardRequest
    {
        public int Count { get; set; } = 100;
    }

    public class GetClanLeaderboardResponse
    {
        public List<ClanLeaderboardEntry> Clans { get; set; }
    }

    public class ClanLeaderboardEntry
    {
        public string ClanId { get; set; }
        public string ClanName { get; set; }
        public int Rank { get; set; }
        public int TotalReputation { get; set; }
    }

    // Data Models
    public class ClanBuildings
    {
        public int BathHouseLevel { get; set; } = 1;
        public int TeaHouseLevel { get; set; } = 1;
        public int TrainingCentreLevel { get; set; } = 1;
    }

    public class ClanMemberInfo
    {
        public string EntityId { get; set; }
        public string PlayFabId { get; set; }
        public string DisplayName { get; set; }
        public int CurrentStamina { get; set; }
        public int MaxStamina { get; set; }
        public int Reputation { get; set; }
        public string Role { get; set; }
    }

    public class ClanStatus
    {
        public int TotalStamina { get; set; }
        public int MaxPossibleStamina { get; set; }
        public double BleedingPercentage { get; set; }
        public bool IsBleeding { get; set; }
    }

    public class PlayerStaminaData
    {
        public int Stamina { get; set; }
        public int MaxStamina { get; set; }
        public long StaminaLastUpdate { get; set; }
        public int Reputation { get; set; }
    }

    // Enums
    public enum BuildingType
    {
        BathHouse,
        TeaHouse,
        TrainingCentre
    }

    // Constants
    public static class ClanConstants
    {
        // Stamina
        public const int BASE_MAX_STAMINA = 100;
        public const int PERSONAL_UPGRADE_MAX_STAMINA = 100;
        public const int BATHHOUSE_STAMINA_PER_LEVEL = 20;
        public const double STAMINA_REGEN_PER_SECOND = 1.0 / 60.0; // 1 stamina per minute

        // Attack costs
        public const int ATTACK_STAMINA_COST = 10;

        // Bleeding threshold
        public const double BLEEDING_THRESHOLD = 0.3; // 30% or less is bleeding

        // Building costs (gold)
        public const int BUILDING_UPGRADE_BASE_COST = 1000;
        public const double BUILDING_UPGRADE_COST_MULTIPLIER = 1.5;

        // Building levels
        public const int MAX_BUILDING_LEVEL = 5;
        public const int MIN_BUILDING_LEVEL = 1;

        // Tea House: reduces stamina per player hit (base value)
        public const int TEAHOUSE_BASE_STAMINA_REDUCTION = 5;
        public const int TEAHOUSE_STAMINA_REDUCTION_PER_LEVEL = 3;

        // Training Centre: number of players hit per attack
        public const int TRAINING_CENTRE_BASE_PLAYERS_HIT = 1;
        public const int TRAINING_CENTRE_PLAYERS_PER_LEVEL = 1;

        // Reputation rewards
        public const int BASE_REPUTATION_REWARD = 3;
        public const int HIGH_RANK_REPUTATION_REWARD = 5;
        public const double REPUTATION_SCALING_FACTOR = 0.01; // 1% per 100 rep difference

        // Statistics names
        public const string STAT_STAMINA = "stamina";
        public const string STAT_MAX_STAMINA = "max_stamina";
        public const string STAT_STAMINA_LAST_UPDATE = "stamina_last_update";
        public const string STAT_REPUTATION = "reputation";
        public const string STAT_CLAN_TOTAL_REPUTATION = "clan_total_reputation";
        public const string STAT_CLAN_GOLD = "clan_gold";

        // Group object names
        public const string OBJ_BUILDINGS = "buildings";
    }
}

