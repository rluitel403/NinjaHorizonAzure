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
            string starterCharacterGrantedKey = "StarterCharacterGranted";

            DateTime today = DateTime.UtcNow;

            // Get user's data to check daily reward progress and starter character flag
            var userData = await playfabUtil.GetUserData(
                new List<string> { dailyRewardProgressKey, energyDataKey, starterCharacterGrantedKey }
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

            // Check if starter character has already been granted
            bool starterCharacterGranted = userData.Data.ContainsKey(starterCharacterGrantedKey) 
                && userData.Data[starterCharacterGrantedKey].Value == "true";
            
            // Grant starter character to new players (only once, ever)
            if (!starterCharacterGranted)
            {
                const string STARTER_CHARACTER_ID = "388725f6-126a-4c5b-b998-dc8896dc500d";
                const string CHARACTER_TYPE = "Entity";
                const int STARTER_TIER = 0; // 1 star character
                
                inventoryOperations.Add(
                    PlayFabUtil.CreateAddNewItemOperation(STARTER_CHARACTER_ID, CHARACTER_TYPE, 1, STARTER_TIER)
                );
                
                inventoryItems.Add(
                    PlayFabUtil.CreateNewInventoryItem(STARTER_CHARACTER_ID, CHARACTER_TYPE, 1, STARTER_TIER)
                );
                
                // Mark starter character as granted
                userDataToUpdate[starterCharacterGrantedKey] = "true";
                
                log.LogInformation($"DailyCheckIn: Granting starter character to new player");
            }

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
                    // Grant item reward (character, weapon, etc.)
                    inventoryOperations.Add(
                        PlayFabUtil.CreateAddNewItemOperation(
                            todayReward.itemId, 
                            todayReward.type, 
                            todayReward.amount, 
                            todayReward.tier
                        )
                    );
                    
                    inventoryItems.Add(
                        PlayFabUtil.CreateNewInventoryItem(
                            todayReward.itemId, 
                            todayReward.type, 
                            todayReward.amount, 
                            todayReward.tier
                        )
                    );
                }

                //reset day if all rewards are claimed
                if (dailyRewardProgress.day >= dailyRewards.Count - 1)
                {
                    dailyRewardProgress.day = -1;
                }
            }
            
            // Execute all inventory operations (starter character + daily rewards if applicable)
            if (inventoryOperations.Count > 0)
            {
                await playfabUtil.ExecuteInventoryOperations(inventoryOperations);
                log.LogInformation($"DailyCheckIn: Granted {inventoryOperations.Count} operations");
            }

            // Always update energy data since we restored it
            userDataToUpdate[energyDataKey] = JsonConvert.SerializeObject(energyData);

            dailyRewardProgress.lastLogin = today.ToString("o");
            userDataToUpdate[dailyRewardProgressKey] = JsonConvert.SerializeObject(
                dailyRewardProgress
            );

            // Update user data
            await playfabUtil.UpdateUserData(userDataToUpdate);

            // Log what we're returning for debugging
            log.LogInformation($"DailyCheckIn: Returning {inventoryItems.Count} inventory items");
            
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
