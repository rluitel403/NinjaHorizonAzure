using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using PlayFab.EconomyModels;

namespace NinjaHorizon.Function
{
    public class DailyRewardItem
    {
        public string type { get; set; }
        public string itemId { get; set; }
        public int amount { get; set; }
    }

    public class DailyRewardProgress
    {
        public string lastLogin { get; set; }
        public int day { get; set; }
    }
    public static class DailyCheckIn
    {
        [FunctionName("DailyCheckIn")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            string dailyRewardProgressKey = "DailyRewardProgress";
            string dailyRewardsTitleDataKey = "dailyRewards";

            // Get user's data to check daily reward progress
            var userData = await playfabUtil.GetUserData(new List<string> { dailyRewardProgressKey });

            // Get title data for rewards configuration
            var titleData = await playfabUtil.GetTitleData(new List<string> { dailyRewardsTitleDataKey });

            DateTime today = DateTime.UtcNow;
            bool isEligible = true;
            DailyRewardProgress dailyRewardProgress = new DailyRewardProgress() { lastLogin = null, day = -1 };

            // Check if user has logged in today
            if (userData.Data.ContainsKey(dailyRewardProgressKey))
            {
                dailyRewardProgress = JsonConvert.DeserializeObject<DailyRewardProgress>(userData.Data[dailyRewardProgressKey].Value);
                DateTime lastLoginDate = DateTime.Parse(dailyRewardProgress.lastLogin);
                isEligible = lastLoginDate.Date < today.Date;
            }

            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>();
            List<InventoryItem> inventoryItems = new List<InventoryItem>();
            string coinItemId = "bf180bae-b805-43c9-99db-e2e8fc1a0719";

            if (isEligible)
            {
                // Get current daily reward day
                dailyRewardProgress.day = dailyRewardProgress.day + 1;

                inventoryOperations.Add(new InventoryOperation
                {
                    Add = new AddInventoryItemsOperation
                    {
                        Item = new InventoryItemReference { Id = coinItemId },
                        Amount = 1
                    }
                });
                inventoryItems.Add(new InventoryItem { Id = coinItemId, Amount = 1 });

                // Get and grant daily reward
                var dailyRewards = JsonConvert.DeserializeObject<List<DailyRewardItem>>(
                    titleData.Data[dailyRewardsTitleDataKey]);
                var todayReward = dailyRewards[dailyRewardProgress.day % dailyRewards.Count];
                string stackId = getStackIdFromType(todayReward.type);

                inventoryOperations.Add(new InventoryOperation
                {
                    Add = new AddInventoryItemsOperation
                    {
                        Item = new InventoryItemReference { Id = todayReward.itemId, StackId = stackId },
                        Amount = todayReward.amount
                    }
                });
                inventoryItems.Add(new InventoryItem { Id = todayReward.itemId, StackId = stackId, Amount = todayReward.amount });

                // Execute inventory operations
                await playfabUtil.ExecuteInventoryOperations(inventoryOperations);
            }

            dailyRewardProgress.lastLogin = today.ToString();

            // Update daily reward progress
            await playfabUtil.UpdateUserData(new Dictionary<string, string>
            {
                { dailyRewardProgressKey, JsonConvert.SerializeObject(dailyRewardProgress) }
            });

            return JsonConvert.SerializeObject(
                new { inventoryItems, isEligible }
            );
        }

        public static string getStackIdFromType(string type)
        {
            if (type == "Weapon") return Guid.NewGuid().ToString();
            return null;
        }
    }
}
