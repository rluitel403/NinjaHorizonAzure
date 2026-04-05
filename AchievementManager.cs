using System;
using System.Collections.Generic;
using System.Linq;
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
    #region Data Models
    
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

    public class AchievementData
    {
        public Dictionary<int, AchievementTracker> Trackers { get; set; }
        public List<AchievementInfo> Infos { get; set; }
    }
    
    #endregion

    /// <summary>
    /// Azure Function for managing player achievements - tracking progress and claiming rewards
    /// </summary>
    public static class AchievementManager
    {
        #region Constants
        
        private const string USER_DATA_KEY = "Achievements";
        private const string TITLE_DATA_KEY = "achievements";
        private const string ACTION_UPDATE = "update";
        private const string ACTION_CLAIM = "claim";
        
        // Social achievement IDs that can be claimed without progress tracking
        private static readonly HashSet<int> SocialAchievementIds = new HashSet<int> { 2 };
        
        #endregion

        #region Main Entry Point
        
        [FunctionName("AchievementManager")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            try
            {
                var context = await PlayFabUtil.ParseFunctionContext(req);
                var playfabUtil = PlayFabUtil.InitializeFromContext(context);
                var input = ParseInput(context);

                var achievementData = await LoadAchievementDataAsync(playfabUtil);

                return input.action switch
                {
                    ACTION_UPDATE => await UpdateAchievementAsync(playfabUtil, input, achievementData),
                    ACTION_CLAIM => await ClaimAchievementAsync(playfabUtil, input, achievementData),
                    _ => throw new ArgumentException($"Invalid action: {input.action}. Must be '{ACTION_UPDATE}' or '{ACTION_CLAIM}'.")
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error in AchievementManager");
                throw;
            }
        }
        
        #endregion

        #region Input Parsing
        
        private static AchievementInput ParseInput(dynamic context)
        {
            return JsonConvert.DeserializeObject<AchievementInput>(
                context.FunctionArgument.ToString()
            );
        }
        
        #endregion

        #region Data Loading
        
        private static async Task<AchievementData> LoadAchievementDataAsync(PlayFabUtil playfabUtil)
        {
            var combinedInfoResult = await playfabUtil.GetPlayerCombinedInfo(
                new List<string> { USER_DATA_KEY.ToLower() },
                new List<string> { USER_DATA_KEY },
                null
            );

            var trackers = LoadAchievementTrackers(combinedInfoResult.InfoResultPayload.UserData);
            var infos = LoadAchievementInfos(combinedInfoResult.InfoResultPayload.TitleData);

            return new AchievementData
            {
                Trackers = trackers,
                Infos = infos
            };
        }

        private static Dictionary<int, AchievementTracker> LoadAchievementTrackers(
            Dictionary<string, UserDataRecord> userData
        )
        {
            if (userData.ContainsKey(USER_DATA_KEY))
            {
                return JsonConvert.DeserializeObject<Dictionary<int, AchievementTracker>>(
                    userData[USER_DATA_KEY].Value
                ) ?? new Dictionary<int, AchievementTracker>();
            }
            return new Dictionary<int, AchievementTracker>();
        }

        private static List<AchievementInfo> LoadAchievementInfos(
            Dictionary<string, string> titleData
        )
        {
            if (titleData.ContainsKey(TITLE_DATA_KEY))
            {
                return JsonConvert.DeserializeObject<List<AchievementInfo>>(
                    titleData[TITLE_DATA_KEY]
                ) ?? new List<AchievementInfo>();
            }
            return new List<AchievementInfo>();
        }
        
        #endregion

        #region Update Achievement
        
        /// <summary>
        /// Increments achievement progress by 1. Creates tracker if it doesn't exist.
        /// Marks as completed when maxProgress is reached.
        /// </summary>
        private static async Task<string> UpdateAchievementAsync(
            PlayFabUtil playfabUtil,
            AchievementInput input,
            AchievementData data
        )
        {
            var achievementInfo = FindAchievementInfo(data.Infos, input.id);
            if (achievementInfo == null)
            {
                throw new ArgumentException($"Achievement with ID {input.id} not found.");
            }

            // Get or create tracker
            if (!data.Trackers.ContainsKey(input.id))
            {
                data.Trackers[input.id] = new AchievementTracker
                {
                    progress = 0,
                    completed = false,
                    claimed = false
                };
            }

            var tracker = data.Trackers[input.id];

            // Don't update if already completed
            if (tracker.completed)
            {
                return JsonConvert.SerializeObject(new { achievements = data.Trackers });
            }

            // Increment progress
            tracker.progress++;
            tracker.completed = tracker.progress >= achievementInfo.maxProgress;

            // Save to PlayFab
            await SaveAchievementTrackersAsync(playfabUtil, data.Trackers);

            return JsonConvert.SerializeObject(new { achievements = data.Trackers });
        }
        
        #endregion

        #region Claim Achievement
        
        /// <summary>
        /// Claims achievement rewards if conditions are met.
        /// Grants items and marks achievement as claimed.
        /// </summary>
        private static async Task<string> ClaimAchievementAsync(
            PlayFabUtil playfabUtil,
            AchievementInput input,
            AchievementData data
        )
        {
            var achievementInfo = FindAchievementInfo(data.Infos, input.id);
            if (achievementInfo == null)
            {
                throw new ArgumentException($"Achievement with ID {input.id} not found.");
            }

            bool isSocialAchievement = IsSocialAchievement(input.id);

            // Validate claim conditions
            ValidateClaimConditions(input.id, data.Trackers, achievementInfo, isSocialAchievement);

            // Handle social achievement (create tracker if needed)
            if (isSocialAchievement && !data.Trackers.ContainsKey(input.id))
            {
                data.Trackers[input.id] = new AchievementTracker
                {
                    progress = achievementInfo.maxProgress,
                    completed = true,
                    claimed = false
                };
            }

            // Grant rewards
            var inventoryItems = await GrantAchievementRewardsAsync(
                playfabUtil,
                achievementInfo.rewards
            );

            // Mark as claimed
            data.Trackers[input.id].claimed = true;
            await SaveAchievementTrackersAsync(playfabUtil, data.Trackers);

            return JsonConvert.SerializeObject(
                new { inventoryItems, achievements = data.Trackers }
            );
        }

        private static void ValidateClaimConditions(
            int achievementId,
            Dictionary<int, AchievementTracker> trackers,
            AchievementInfo achievementInfo,
            bool isSocialAchievement
        )
        {
            // Social achievements can be claimed without prior progress
            if (isSocialAchievement)
            {
                return;
            }

            // Check if tracker exists
            if (!trackers.ContainsKey(achievementId))
            {
                throw new InvalidOperationException(
                    $"Achievement {achievementId} has no progress tracked."
                );
            }

            var tracker = trackers[achievementId];

            // Check if already claimed
            if (tracker.claimed)
            {
                throw new InvalidOperationException(
                    $"Achievement {achievementId} has already been claimed."
                );
            }

            // Check if completed
            if (tracker.progress < achievementInfo.maxProgress)
            {
                throw new InvalidOperationException(
                    $"Achievement {achievementId} is not completed yet. Progress: {tracker.progress}/{achievementInfo.maxProgress}"
                );
            }
        }

        private static async Task<List<InventoryItem>> GrantAchievementRewardsAsync(
            PlayFabUtil playfabUtil,
            List<DailyRewardItem> rewards
        )
        {
            var inventoryOperations = new List<InventoryOperation>();
            var inventoryItems = new List<InventoryItem>();

            foreach (var reward in rewards)
            {
                inventoryOperations.Add(
                    PlayFabUtil.CreateAddNewItemOperation(
                        reward.itemId, 
                        reward.type, 
                        reward.amount, 
                        reward.tier
                    )
                );
                
                inventoryItems.Add(
                    PlayFabUtil.CreateNewInventoryItem(
                        reward.itemId, 
                        reward.type, 
                        reward.amount, 
                        reward.tier
                    )
                );
            }

            await playfabUtil.ExecuteInventoryOperations(inventoryOperations);
            return inventoryItems;
        }
        
        #endregion

        #region Helper Methods
        
        private static AchievementInfo FindAchievementInfo(List<AchievementInfo> infos, int id)
        {
            return infos.FirstOrDefault(a => a.id == id);
        }

        private static bool IsSocialAchievement(int achievementId)
        {
            return SocialAchievementIds.Contains(achievementId);
        }

        private static async Task SaveAchievementTrackersAsync(
            PlayFabUtil playfabUtil,
            Dictionary<int, AchievementTracker> trackers
        )
        {
            var serialized = JsonConvert.SerializeObject(trackers);
            await playfabUtil.UpdateUserData(
                new Dictionary<string, string> { { USER_DATA_KEY, serialized } }
            );
        }
        
        #endregion
    }
}
