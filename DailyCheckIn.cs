using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PlayFab.EconomyModels;
using PlayFab.ServerModels;

namespace NinjaHorizon.Function
{
    public class DailyRewardItem
    {
        public string type { get; set; }
        public string itemId { get; set; }
        public int amount { get; set; }
        public int tier { get; set; } //if its entity or weapon
    }

    public class DailyRewardProgress
    {
        public string lastLogin { get; set; }
        public int day { get; set; }
    }

    public class AchievementTracker
    {
        public int id { get; set; }
        public int progress { get; set; }
        public bool completed { get; set; }

        public bool claimed { get; set; }
    }

    public static class DailyCheckIn
    {

        [FunctionName("DailyCheckIn")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            string dailyRewardProgressKey = "DailyRewardProgress";
            string energyDataKey = "EnergyData";
            string dailyRewardsTitleDataKey = "dailyRewards";

            DateTime today = DateTime.UtcNow;

            // Get user's data to check daily reward progress
            var userData = await playfabUtil.GetUserData(
                new List<string> { dailyRewardProgressKey, energyDataKey }
            );

            // Handle energy restoration first
            EnergyData energyData;
            if (userData.Data.ContainsKey(energyDataKey))
            {
                energyData = JsonConvert.DeserializeObject<EnergyData>(
                    userData.Data[energyDataKey].Value
                );
                energyData = EnergySystem.RestoreEnergy(energyData, today);
            }
            else
            {
                energyData = new EnergyData()
                {
                    currentEnergy = 100,
                    maxEnergy = 100,
                    lastUpdatedTime = today.ToString("o")
                };
            }

            // Get title data for rewards configuration
            var titleData = await playfabUtil.GetTitleData(
                new List<string> { dailyRewardsTitleDataKey }
            );

            bool isEligible = true;
            DailyRewardProgress dailyRewardProgress = new DailyRewardProgress()
            {
                lastLogin = null,
                day = -1
            };
            // Check if user has logged in today
            if (userData.Data.ContainsKey(dailyRewardProgressKey))
            {
                dailyRewardProgress = JsonConvert.DeserializeObject<DailyRewardProgress>(
                    userData.Data[dailyRewardProgressKey].Value
                );
                DateTime lastLoginDate = DateTime.Parse(
                    dailyRewardProgress.lastLogin,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind
                );
                isEligible = lastLoginDate.Date < today.Date;
            }
            Dictionary<string, string> userDataToUpdate = new Dictionary<string, string>();
            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>();
            List<InventoryItem> inventoryItems = new List<InventoryItem>();

            if (isEligible)
            {
                // Get current daily reward day
                dailyRewardProgress.day = dailyRewardProgress.day + 1;

                inventoryOperations.Add(
                    new InventoryOperation
                    {
                        Add = new AddInventoryItemsOperation
                        {
                            Item = new InventoryItemReference { Id = InventoryUtil.COIN_ID },
                            Amount = 1
                        }
                    }
                );
                inventoryItems.Add(new InventoryItem { Id = InventoryUtil.COIN_ID, Amount = 1 });

                // Get and grant daily reward
                var dailyRewards = JsonConvert.DeserializeObject<List<DailyRewardItem>>(
                    titleData.Data[dailyRewardsTitleDataKey]
                );
                var todayReward = dailyRewards[dailyRewardProgress.day];
                if (todayReward.type == "Energy")
                {
                    // No need to deserialize energy data again since we already have it
                    energyData.currentEnergy += todayReward.amount;
                }
                else
                {
                    string stackId = PlayFabUtil.GetStackIdFromType(todayReward.type);

                    inventoryOperations.Add(
                        new InventoryOperation
                        {
                            Add = new AddInventoryItemsOperation
                            {
                                Item = new InventoryItemReference
                                {
                                    Id = todayReward.itemId,
                                    StackId = stackId
                                },
                                Amount = todayReward.amount,
                                //if its entity or weapon, set the tier since we can grant high tier items
                                NewStackValues =
                                    stackId != null
                                        ? new InitialValues
                                        {
                                            DisplayProperties = new { todayReward.tier }
                                        }
                                        : null
                            }
                        }
                    );
                    inventoryItems.Add(
                        new InventoryItem
                        {
                            Id = todayReward.itemId,
                            StackId = stackId,
                            Amount = todayReward.amount,
                            DisplayProperties = stackId != null ? new { todayReward.tier } : null
                        }
                    );
                }

                // Execute inventory operations
                await playfabUtil.ExecuteInventoryOperations(inventoryOperations);

                //reset day if all rewards are claimed
                if (dailyRewardProgress.day >= dailyRewards.Count - 1)
                {
                    dailyRewardProgress.day = -1;
                }
            }

            // Always update energy data since we restored it
            userDataToUpdate[energyDataKey] = JsonConvert.SerializeObject(energyData);

            dailyRewardProgress.lastLogin = today.ToString("o");
            userDataToUpdate[dailyRewardProgressKey] = JsonConvert.SerializeObject(
                dailyRewardProgress
            );

            // Update user data
            await playfabUtil.UpdateUserData(userDataToUpdate);

            return JsonConvert.SerializeObject(
                new
                {
                    inventoryItems,
                    dailyRewardProgress,
                    energyData
                }
            );
        }
    }
}
