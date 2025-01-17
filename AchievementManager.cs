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
    public class AchievementInput
    {
        public int id { get; set; }
        public string action { get; set; }
    }

    public class AchievementInfo
    {
        public int id { get; set; }
        public int maxProgress { get; set; }
        public string name { get; set; }
        public List<DailyRewardItem> rewards { get; set; }
    }

    public static class AchievementManager
    {
        [FunctionName("AchievementManager")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            AchievementInput achievementInput = JsonConvert.DeserializeObject<AchievementInput>(
                context.FunctionArgument.ToString()
            );

            // Get achievement data first
            var (achievements, achievementInfos) = await GetAchievementData(playfabUtil);

            if (achievementInput.action == "update")
            {
                return await UpdateAchievement(
                    playfabUtil,
                    achievementInput,
                    achievements,
                    achievementInfos
                );
            }
            else if (achievementInput.action == "claim")
            {
                return await ClaimAchievement(
                    playfabUtil,
                    achievementInput,
                    achievements,
                    achievementInfos
                );
            }
            throw new Exception("Invalid action");
        }

        private static async Task<(
            Dictionary<int, AchievementTracker>,
            List<AchievementInfo>
        )> GetAchievementData(PlayFabUtil playfabUtil)
        {
            //get player combined info from playfab util
            var combinedInfoResult = await playfabUtil.GetPlayerCombinedInfo(
                new List<string> { "achievements" },
                new List<string> { "Achievements" },
                null
            );

            Dictionary<int, AchievementTracker> achievements =
                new Dictionary<int, AchievementTracker>();
            List<AchievementInfo> achievementInfos = new List<AchievementInfo>();

            var userData = combinedInfoResult.InfoResultPayload.UserData;
            var titleData = combinedInfoResult.InfoResultPayload.TitleData;
            if (userData.ContainsKey("Achievements"))
            {
                achievements = JsonConvert.DeserializeObject<Dictionary<int, AchievementTracker>>(
                    userData["Achievements"].Value
                );
            }
            if (titleData.ContainsKey("achievements"))
            {
                achievementInfos = JsonConvert.DeserializeObject<List<AchievementInfo>>(
                    titleData["achievements"].ToString()
                );
            }

            return (achievements, achievementInfos);
        }

        public static async Task<string> UpdateAchievement(
            PlayFabUtil playfabUtil,
            AchievementInput achievementInput,
            Dictionary<int, AchievementTracker> achievements,
            List<AchievementInfo> achievementInfos
        )
        {
            // Move achievement update logic here
            if (achievements.ContainsKey(achievementInput.id))
            {
                achievements[achievementInput.id].progress += 1;
            }
            else
            {
                var achievementTracker = new AchievementTracker { progress = 1 };
                achievements.Add(achievementInput.id, achievementTracker);
            }

            var achievementInfo = achievementInfos.Find(a => a.id == achievementInput.id);

            achievements[achievementInput.id].completed =
                achievements[achievementInput.id].progress >= achievementInfo.maxProgress;

            var serializedAchievements = JsonConvert.SerializeObject(achievements);
            await playfabUtil.UpdateUserData(
                new Dictionary<string, string> { { "Achievements", serializedAchievements } }
            );

            return JsonConvert.SerializeObject(new { achievements });
        }

        private static async Task<string> ClaimAchievement(
            PlayFabUtil playfabUtil,
            AchievementInput achievementInput,
            Dictionary<int, AchievementTracker> achievements,
            List<AchievementInfo> achievementInfos
        )
        {
            // Implement claim logic here
            var achievementInfo = achievementInfos.Find(a => a.id == achievementInput.id);
            List<int> socialAchievements = new List<int> { 2 };
            bool isSocialAchievement = socialAchievements.Contains(achievementInput.id);
            //throw error if achievement is not found, progress is not max progress, or achievement is already claimed
            if (
                !isSocialAchievement
                && (
                    achievementInfo == null
                    || achievements[achievementInput.id]?.progress != achievementInfo.maxProgress
                    || achievements[achievementInput.id]?.claimed == true
                )
            )
            {
                throw new Exception("Cannot claim achievement");
            }
            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>();
            List<InventoryItem> inventoryItems = new List<InventoryItem>();
            //grant the rewards
            foreach (var reward in achievementInfo.rewards)
            {
                string stackId = PlayFabUtil.GetStackIdFromType(reward.type);
                inventoryItems.Add(
                    new InventoryItem
                    {
                        Id = reward.itemId,
                        StackId = stackId,
                        Amount = reward.amount,
                        DisplayProperties = stackId != null ? new { reward.tier } : null
                    }
                );
                inventoryOperations.Add(
                    new InventoryOperation
                    {
                        Add = new AddInventoryItemsOperation
                        {
                            Item = new InventoryItemReference
                            {
                                Id = reward.itemId,
                                StackId = stackId
                            },
                            Amount = reward.amount,
                            NewStackValues =
                                stackId != null
                                    ? new InitialValues { DisplayProperties = new { reward.tier } }
                                    : null
                        }
                    }
                );
            }
            //mark achievement as claimed
            if (isSocialAchievement)
            {
                achievements.Add(
                    achievementInput.id,
                    new AchievementTracker
                    {
                        id = achievementInfo.id,
                        progress = achievementInfo.maxProgress,
                        claimed = true,
                        completed = true
                    }
                );
            }
            achievements[achievementInput.id].claimed = true;
            var serializedAchievements = JsonConvert.SerializeObject(achievements);
            await playfabUtil.UpdateUserData(
                new Dictionary<string, string> { { "Achievements", serializedAchievements } }
            );
            //grant items
            await playfabUtil.ExecuteInventoryOperations(inventoryOperations);

            return JsonConvert.SerializeObject(new { inventoryItems, achievements });
        }
    }
}
