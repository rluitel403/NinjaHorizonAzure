using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
            if (achievementInput.action == "update")
            {
                var achievements = await UpdateAchievement(playfabUtil, achievementInput);
                return achievements;
            }
            else if (achievementInput.action == "claim")
            {
                //var result = await playfabUtil.ClaimAchievement(achievementInput.id);
                throw new Exception("Not implemented");
            }
            throw new Exception("Invalid action");
        }

        public static async Task<string> UpdateAchievement(
            PlayFabUtil playfabUtil,
            AchievementInput achievementInput
        )
        {
            var combinedInfoResult = await playfabUtil.ServerApi.GetPlayerCombinedInfoAsync(
                new GetPlayerCombinedInfoRequest
                {
                    PlayFabId = playfabUtil.PlayFabId,
                    InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
                    {
                        GetTitleData = true,
                        TitleDataKeys = new List<string> { "achievements" },
                        GetUserData = true,
                        UserDataKeys = new List<string> { "Achievemnts" }
                    }
                }
            );
            AchievementTracker achievementTracker = new AchievementTracker();
            Dictionary<int, AchievementTracker> achievements =
                new Dictionary<int, AchievementTracker>();
            List<AchievementInfo> achievementInfos = new List<AchievementInfo>();
            var userData = combinedInfoResult.Result.InfoResultPayload.UserData;
            var titleData = combinedInfoResult.Result.InfoResultPayload.TitleData;
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
            if (achievements.ContainsKey(achievementInput.id))
            {
                achievements[achievementInput.id].progress += 1;
            }
            else
            {
                achievementTracker.progress = 1;
                achievements.Add(achievementInput.id, achievementTracker);
            }
            achievements[achievementInput.id].completed =
                achievements[achievementInput.id].progress
                >= achievementInfos.Find(a => a.id == achievementInput.id).maxProgress;
            var serializedAchievements = JsonConvert.SerializeObject(achievements);
            //save userData
            await playfabUtil.UpdateUserData(
                new Dictionary<string, string> { { "Achievements", serializedAchievements } }
            );

            return serializedAchievements;
        }
    }
}
