using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PlayFab.EconomyModels;

namespace NinjaHorizon.Function
{
    public class CostItem
    {
        public string itemId;
        public int amount;
    }

    public class TierRequirement
    {
        public int tier;
        public List<CostItem> requirements;
    }

    public class UpgradeCosts
    {
        public List<TierRequirement> characterUpgradeCost;
        public List<TierRequirement> itemUpgradeCost;
    }

    public class Tier
    {
        public int tier { get; set; }
    }

    public static class InventoryUtil
    {
        public const string GOLD_ID = "b4a8100a-73a4-47e5-85e1-23a56a66a313";
        public const string TOKEN_ID = "349f39f1-fc39-424b-a44f-ddfdf39a171c";

        public const string COIN_ID = "bf180bae-b805-43c9-99db-e2e8fc1a0719";

        public static bool IsGoldOrToken(string itemId)
        {
            return itemId == GOLD_ID || itemId == TOKEN_ID;
        }

        public static async Task<UpgradeCosts> GetUpgradeCosts(PlayFabUtil playfabUtil)
        {
            var upgradeItemRequirementData = await playfabUtil.GetTitleData(
                new List<string> { "upgradeCosts" }
            );
            return JsonConvert.DeserializeObject<UpgradeCosts>(
                upgradeItemRequirementData.Data["upgradeCosts"].ToString()
            );
        }
    }
}
