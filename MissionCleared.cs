using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PlayFab;
using PlayFab.EconomyModels;
using PlayFab.ServerModels;
using EntityKey = PlayFab.EconomyModels.EntityKey;

namespace NinjaHorizon.Function
{
    public class BattleType
    {
        public const string PVE = "PVE";
        public const string PVP = "PVP";
        public const string PVE_HUNTING_HOUSE = "PVE_HUNTING_HOUSE";
        public const string PVE_TOWER = "PVE_TOWER";
    }

    public class EntityData
    {
        public int xp { get; set; }
        public string weapon { get; set; }
        public string backItem { get; set; }
        public string clothing { get; set; }
        public string artifact { get; set; }
        public int tier { get; set; }
    }

    public class MissionClearedUpdatedData
    {
        public List<StatisticValue> statsUpdate { get; set; }
        public List<InventoryItem> inventoryItems { get; set; }
    }

    public class Extra
    {
        public bool firstTime { get; set; }
        public string id { get; set; }
        public int amount { get; set; }
        public int chance { get; set; }

        public bool stackable { get; set; }
    }

    public class Reward
    {
        public int gold { get; set; }
        public int xp { get; set; }

        public int token { get; set; }

        public int energy { get; set; }

        public List<Extra> extra { get; set; }
    }

    public class Mission
    {
        public int enemyLvl { get; set; }
        public Reward rewards { get; set; }
        public string name { get; set; }
        public string desc { get; set; }
        public List<string> enemies { get; set; }
    }

    public class MissionGrade
    {
        public int mission_grade_id { get; set; }
        public string name { get; set; }
        public List<Mission> missions { get; set; }
    }

    /// <summary>
    /// Request object for mission cleared function
    /// </summary>
    public class MissionClearedRequest
    {
        public int missionGradeId { get; set; }
        public int missionId { get; set; }
        public int difficulty { get; set; }
        public int floorId { get; set; }
        public string battleType { get; set; }
        public List<string> selectedCharacters { get; set; }
    }

    public class MissionContext
    {
        public int MissionGradeId { get; set; }
        public int MissionId { get; set; }
        public int Difficulty { get; set; }
        public int FloorId { get; set; }
        public bool IsMissionOrTower { get; set; }
        public bool IsMission { get; set; }
        public bool IsTower { get; set; }

        public bool IsHuntingHouse { get; set; }
        public string MissionGrade { get; set; }
        public int ScaledMissionId { get; set; }
        public int NumberOfMission { get; set; }

        public MissionContext(MissionClearedRequest request)
        {
            MissionGradeId = request.missionGradeId;
            MissionId = request.missionId;
            Difficulty = request.difficulty;
            FloorId = request.floorId;
            IsHuntingHouse = request.battleType == BattleType.PVE_HUNTING_HOUSE;
            IsMissionOrTower =
                request.battleType == BattleType.PVE || request.battleType == BattleType.PVE_TOWER;
            IsMission = request.battleType == BattleType.PVE;
            IsTower = request.battleType == BattleType.PVE_TOWER;
            MissionGrade = "missiongrade" + MissionGradeId;
            ScaledMissionId = MissionId + MissionRewardConfig.DIFFICULTY_SCALE_MULTIPLIER * Difficulty;
        }
    }

    public class PlayerInfo
    {
        public Dictionary<string, string> TitleData { get; set; }
        public List<StatisticValue> PlayerStatistics { get; set; }
        public Dictionary<string, UserDataRecord> UserData { get; set; }
        public List<string> SelectedCharacters { get; set; }
        public int MaxMissionId { get; set; }
        public List<InventoryItem> Inventory { get; set; }
    }

    public class MissionData
    {
        public List<string> Enemies { get; set; }
        public Reward Rewards { get; set; }
        public int NumberOfMission { get; set; }
    }

    public class RewardContext
    {
        public Reward GrantedRewards { get; set; }
        public int DifficultyChanceBoost { get; set; }
        public float ChanceBoostScaler { get; set; }
        public bool FirstClear { get; set; }
    }

    public class ResultData
    {
        public List<StatisticUpdate> StatsUpdate { get; set; } = new List<StatisticUpdate>();
        public List<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
        public List<InventoryOperation> InventoryOperations { get; set; } =
            new List<InventoryOperation>();
        public Dictionary<string, UserDataRecord> UserDataRecord { get; set; } =
            new Dictionary<string, UserDataRecord>();
    }

    public class PlayFabApiUtil
    {
        public PlayFabServerInstanceAPI ServerApi { get; set; }
        public PlayFabEconomyInstanceAPI EconomyApi { get; set; }
        public EntityKey Entity { get; set; }
        public string PlayFabId { get; set; }
    }

    public class EnergyData
    {
        public int currentEnergy { get; set; }
        public int maxEnergy { get; set; }

        public string lastUpdatedTime { get; set; }
    }

    public static class MissionCleared
    {
        // Shared Random instance to avoid time-based seeding issues
        private static readonly Random _random = new Random();
        
        [FunctionName("MissionCleared")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            
            // Parse request into strongly-typed object
            var request = JsonConvert.DeserializeObject<MissionClearedRequest>(
                context.FunctionArgument.ToString(),
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );

            if (request == null)
            {
                throw new Exception("Invalid mission request data");
            }

            if (request.selectedCharacters == null || request.selectedCharacters.Count == 0)
            {
                throw new Exception("No characters selected for mission");
            }

            var missionContext = new MissionContext(request);
            var playerInfo = await GetPlayerInfo(playfabUtil, missionContext, request.selectedCharacters);
            var resultData = new ResultData();

            ValidateEnergy(playerInfo.UserData, resultData, missionContext);

            var missionData = ParseMissionData(playerInfo.TitleData["missions"], missionContext);
            missionContext.NumberOfMission = missionData.NumberOfMission;
            ValidatePlayerCharacters(playerInfo.Inventory, playerInfo.SelectedCharacters);

            var rewardContext = CalculateRewards(
                missionContext,
                missionData,
                playerInfo.MaxMissionId
            );

            UpdateProgress(missionContext, playerInfo, rewardContext, resultData);
            GrantRewards(
                missionContext,
                missionData,
                rewardContext,
                playerInfo.Inventory,
                resultData
            );
            await UpdatePlayerData(playfabUtil, resultData);

            return JsonConvert.SerializeObject(
                new
                {
                    inventoryItems = resultData.InventoryItems,
                    statsUpdate = resultData.StatsUpdate,
                    userDataRecord = resultData.UserDataRecord,
                    grantedRewards = rewardContext.GrantedRewards,
                }
            );
        }

        private static async Task<PlayerInfo> GetPlayerInfo(
            PlayFabUtil playfabUtil,
            MissionContext missionContext,
            List<string> selectedCharacters
        )
        {
            var combinedInfoResult = await playfabUtil.ServerApi.GetPlayerCombinedInfoAsync(
                new GetPlayerCombinedInfoRequest
                {
                    PlayFabId = playfabUtil.PlayFabId,
                    InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
                    {
                        GetPlayerStatistics = missionContext.IsMissionOrTower,
                        PlayerStatisticNames = new List<string> { missionContext.MissionGrade },
                        GetTitleData = true,
                        TitleDataKeys = new List<string> { "missions" },
                        GetUserData = true,
                        UserDataKeys = new List<string>
                        {
                            "HuntingHouseProgression",
                            "SelectedCharacters",
                            "EnergyData"
                        }
                    }
                }
            );

            // Validate that client-sent characters match server-stored selection
            List<string> serverSelectedCharacters = null;
            if (combinedInfoResult.Result.InfoResultPayload.UserData.ContainsKey("SelectedCharacters"))
            {
                serverSelectedCharacters = JsonConvert.DeserializeObject<List<string>>(
                    combinedInfoResult.Result.InfoResultPayload.UserData["SelectedCharacters"].Value,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );

                // Validate client selection matches server
                if (serverSelectedCharacters != null && 
                    (selectedCharacters.Count != serverSelectedCharacters.Count ||
                     !selectedCharacters.All(serverSelectedCharacters.Contains)))
                {
                    throw new Exception("Selected characters mismatch between client and server");
                }
            }

            var playerInfo = new PlayerInfo
            {
                TitleData = combinedInfoResult.Result.InfoResultPayload.TitleData,
                PlayerStatistics = combinedInfoResult.Result.InfoResultPayload.PlayerStatistics,
                UserData = combinedInfoResult.Result.InfoResultPayload.UserData,
                SelectedCharacters = selectedCharacters
            };

            playerInfo.MaxMissionId = missionContext.IsMissionOrTower
                ? GetMissionOrTowerMaxMissionId(
                    playerInfo.PlayerStatistics,
                    missionContext.MissionGrade,
                    missionContext.ScaledMissionId
                )
                : GetHuntingHouseMaxBossFloor(
                    playerInfo.UserData,
                    missionContext.MissionGradeId,
                    missionContext.MissionId,
                    missionContext.FloorId
                );

            playerInfo.Inventory = await GetPlayerInventory(
                playfabUtil,
                playerInfo.SelectedCharacters
            );

            return playerInfo;
        }

        private static async Task<List<InventoryItem>> GetPlayerInventory(
            PlayFabUtil playfabUtil,
            List<string> selectedCharacters
        )
        {
            string filter = string.Join(
                " or ",
                selectedCharacters.Select(c => $"stackId eq '{c}'")
            );

            var getInventoryItemsRequest = new GetInventoryItemsRequest
            {
                Entity = playfabUtil.Entity,
                CollectionId = "default",
                Filter = filter
            };

            var inventory = await playfabUtil.EconomyApi.GetInventoryItemsAsync(
                getInventoryItemsRequest
            );
            return inventory.Result.Items;
        }

        private static MissionData ParseMissionData(
            string missionGrades,
            MissionContext missionContext
        )
        {
            if (missionContext.IsTower)
            {
                return new MissionData
                {
                    Enemies = new List<string>(),
                    Rewards = new Reward { extra = new List<Extra>() },
                    NumberOfMission = 0
                };
            }

            var missionGradesList = JsonConvert.DeserializeObject<List<MissionGrade>>(
                missionGrades,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );

            if (missionGradesList == null || missionGradesList.Count == 0)
            {
                throw new Exception("Mission grades data not found");
            }

            var missionGradeData = missionGradesList.Find(mg =>
                mg.mission_grade_id == missionContext.MissionGradeId
            );

            if (missionGradeData == null)
            {
                throw new Exception($"Mission grade {missionContext.MissionGradeId} not found");
            }

            if (missionContext.MissionId >= missionGradeData.missions.Count)
            {
                throw new Exception($"Mission {missionContext.MissionId} not found in grade {missionContext.MissionGradeId}");
            }

            var mission = missionGradeData.missions[missionContext.MissionId];

            return new MissionData
            {
                Enemies = mission.enemies ?? new List<string>(),
                Rewards = mission.rewards ?? new Reward { extra = new List<Extra>() },
                NumberOfMission = missionGradeData.missions.Count - 1
            };
        }

        private static void ValidatePlayerCharacters(
            List<InventoryItem> inventory,
            List<string> selectedCharacters
        )
        {
            if (inventory.Count != selectedCharacters.Count)
            {
                throw new Exception("Selected characters not found in inventory");
            }
        }

        private static RewardContext CalculateRewards(
            MissionContext missionContext,
            MissionData missionData,
            int maxMissionId
        )
        {
            Reward grantedRewards;
            float chanceBoostScaler;
            int difficultyChanceBoost;

            if (missionContext.IsMission)
            {
                float rewardMultiplier = MissionRewardConfig.GetRewardMultiplier(missionContext.Difficulty);
                grantedRewards = new Reward
                {
                    extra = new List<Extra>(),
                    xp = (int)(missionData.Rewards.xp * rewardMultiplier),
                    gold = (int)(missionData.Rewards.gold * rewardMultiplier),
                    token = 0
                };
                difficultyChanceBoost = MissionRewardConfig.GetDropChanceBoost(missionContext.Difficulty);
                chanceBoostScaler = MissionRewardConfig.GetChanceScaler(missionContext.Difficulty);
            }
            else if (missionContext.IsTower)
            {
                int floorNumber = missionContext.MissionId + 1;
                grantedRewards = new Reward
                {
                    extra = new List<Extra>(),
                    gold = floorNumber * MissionRewardConfig.TOWER_GOLD_MULTIPLIER,
                    xp = floorNumber * MissionRewardConfig.TOWER_XP_MULTIPLIER,
                    token = floorNumber % MissionRewardConfig.TOWER_TOKEN_FREQUENCY == 0 
                        ? MissionRewardConfig.TOWER_TOKEN_AMOUNT 
                        : 0
                };
                difficultyChanceBoost = MissionRewardConfig.GetDropChanceBoost(missionContext.Difficulty);
                chanceBoostScaler = MissionRewardConfig.GetChanceScaler(missionContext.Difficulty);
            }
            else // Hunting House
            {
                float rewardScaler = MissionRewardConfig.GetHuntingHouseRewardScaler(missionContext.FloorId);
                grantedRewards = new Reward
                {
                    extra = new List<Extra>(),
                    xp = (int)(MissionRewardConfig.HUNTING_HOUSE_BASE_XP * rewardScaler),
                    gold = (int)(MissionRewardConfig.HUNTING_HOUSE_BASE_GOLD * rewardScaler),
                    token = 0
                };
                difficultyChanceBoost = MissionRewardConfig.GetHuntingHouseDropChance(missionContext.FloorId);
                chanceBoostScaler = MissionRewardConfig.GetHuntingHouseChanceScaler(missionContext.FloorId);
            }

            return new RewardContext
            {
                GrantedRewards = grantedRewards,
                DifficultyChanceBoost = difficultyChanceBoost,
                ChanceBoostScaler = chanceBoostScaler,
                FirstClear = missionContext.IsMissionOrTower
                    ? maxMissionId == missionContext.ScaledMissionId
                    : missionContext.FloorId == maxMissionId
            };
        }

        private static void UpdateProgress(
            MissionContext missionContext,
            PlayerInfo playerInfo,
            RewardContext rewardContext,
            ResultData resultData
        )
        {
            if (rewardContext.FirstClear)
            {
                if (missionContext.IsMission)
                {
                    UpdateVillageMissionProgress(missionContext, resultData);
                }
                else if (missionContext.IsTower)
                {
                    UpdateTowerProgress(missionContext, resultData);
                }
                else if (missionContext.IsHuntingHouse)
                {
                    UpdateHuntingHouseProgress(missionContext, playerInfo, resultData);
                }
            }
        }

        private static void UpdateTowerProgress(
            MissionContext missionContext,
            ResultData resultData
        )
        {
            resultData.StatsUpdate.Add(
                new StatisticUpdate { StatisticName = missionContext.MissionGrade, Value = 1 }
            );
        }

        private static void UpdateVillageMissionProgress(
            MissionContext missionContext,
            ResultData resultData
        )
        {
            if (
                missionContext.MissionId == missionContext.NumberOfMission
                && missionContext.Difficulty == 0
            )
            {
                resultData.StatsUpdate.Add(
                    new StatisticUpdate
                    {
                        StatisticName = $"missiongrade{missionContext.MissionGradeId + 1}",
                        Value = 0
                    }
                );
            }

            resultData.StatsUpdate.Add(
                new StatisticUpdate { StatisticName = missionContext.MissionGrade, Value = 1 }
            );
        }

        private static void UpdateHuntingHouseProgress(
            MissionContext missionContext,
            PlayerInfo playerInfo,
            ResultData resultData
        )
        {
            var huntingHouseData = new Dictionary<string, Dictionary<int, int>>();
            if (playerInfo.UserData.ContainsKey("HuntingHouseProgression"))
            {
                huntingHouseData = JsonConvert.DeserializeObject<
                    Dictionary<string, Dictionary<int, int>>
                >(
                    playerInfo.UserData["HuntingHouseProgression"].Value,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );
            }

            if (!huntingHouseData.ContainsKey(missionContext.MissionGrade))
            {
                huntingHouseData[missionContext.MissionGrade] = new Dictionary<int, int>();
            }

            int currentMaxFloorId = huntingHouseData[missionContext.MissionGrade]
                .GetValueOrDefault(missionContext.MissionId, 0);
            huntingHouseData[missionContext.MissionGrade][missionContext.MissionId] = Math.Max(
                currentMaxFloorId,
                missionContext.FloorId + 1
            );

            var huntingHouseProgressionSerialized = JsonConvert.SerializeObject(huntingHouseData);
            resultData.UserDataRecord["HuntingHouseProgression"] = new UserDataRecord
            {
                Value = huntingHouseProgressionSerialized
            };
        }

        private static void GrantRewards(
            MissionContext missionContext,
            MissionData missionData,
            RewardContext rewardContext,
            List<InventoryItem> inventory,
            ResultData resultData
        )
        {
            GrantPlayerXpAndGold(inventory, rewardContext.GrantedRewards, resultData);
            GrantExtraRewards(missionContext, missionData, rewardContext, resultData);
            TryGrantEnergyReward(rewardContext, resultData);
        }

        private static void TryGrantEnergyReward(RewardContext rewardContext, ResultData resultData)
        {
            int randomNumber = _random.Next(1, 101);
            if (randomNumber <= MissionRewardConfig.ENERGY_DROP_CHANCE)
            {
                //energy data is in resultData.UserDataRecord since we used it before
                var energyData = JsonConvert.DeserializeObject<EnergyData>(
                    resultData.UserDataRecord["EnergyData"].Value,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );
                energyData.currentEnergy += MissionRewardConfig.ENERGY_DROP_AMOUNT;
                resultData.UserDataRecord["EnergyData"] = new UserDataRecord
                {
                    Value = JsonConvert.SerializeObject(energyData)
                };
                rewardContext.GrantedRewards.energy = MissionRewardConfig.ENERGY_DROP_AMOUNT;
            }
            else
            {
                rewardContext.GrantedRewards.energy = 0;
            }
        }

        private static void GrantPlayerXpAndGold(
            List<InventoryItem> inventory,
            Reward grantedRewards,
            ResultData resultData
        )
        {
            foreach (var item in inventory)
            {
                var entityData = JsonConvert.DeserializeObject<EntityData>(
                    item.DisplayProperties.ToString(),
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );
                entityData.xp += grantedRewards.xp;
                var updateItem = new InventoryItem
                {
                    Id = item.Id,
                    StackId = item.StackId,
                    DisplayProperties = entityData,
                    Amount = item.Amount
                };
                resultData.InventoryOperations.Add(
                    new InventoryOperation
                    {
                        Update = new UpdateInventoryItemsOperation { Item = updateItem }
                    }
                );
                resultData.InventoryItems.Add(updateItem);
            }

            resultData.StatsUpdate.Add(
                new StatisticUpdate { StatisticName = "xp", Value = grantedRewards.xp }
            );

            if (grantedRewards.gold > 0)
            {
                resultData.InventoryOperations.Add(
                    new InventoryOperation
                    {
                        Add = new AddInventoryItemsOperation
                        {
                            Item = new InventoryItemReference { Id = InventoryUtil.GOLD_ID },
                            Amount = grantedRewards.gold
                        }
                    }
                );
                resultData.InventoryItems.Add(
                    new InventoryItem { Id = InventoryUtil.GOLD_ID, Amount = grantedRewards.gold }
                );
            }
            if (grantedRewards.token > 0)
            {
                resultData.InventoryOperations.Add(
                    new InventoryOperation
                    {
                        Add = new AddInventoryItemsOperation
                        {
                            Item = new InventoryItemReference { Id = InventoryUtil.TOKEN_ID },
                            Amount = grantedRewards.token
                        }
                    }
                );
                resultData.InventoryItems.Add(
                    new InventoryItem { Id = InventoryUtil.TOKEN_ID, Amount = grantedRewards.token }
                );
            }
        }

        private static void GrantExtraRewards(
            MissionContext missionContext,
            MissionData missionData,
            RewardContext rewardContext,
            ResultData resultData
        )
        {
            if (missionContext.IsMission)
            {
                GrantMissionEnemyReward(missionData, rewardContext, resultData);
            }

            foreach (var extra in missionData.Rewards.extra ?? new List<Extra>())
            {
                GrantExtraReward(extra, rewardContext, resultData);
            }
        }

        private static void GrantMissionEnemyReward(
            MissionData missionData,
            RewardContext rewardContext,
            ResultData resultData
        )
        {
            if (missionData.Enemies == null || missionData.Enemies.Count == 0)
            {
                return;
            }

            int randomChance = _random.Next(1, 101);
            if (randomChance <= rewardContext.DifficultyChanceBoost)
            {
                int enemyTier = MissionRewardConfig.GetEnemyTier(0); // Default to normal difficulty tier
                string enemyId = missionData.Enemies[_random.Next(0, missionData.Enemies.Count)];
                
                AddToInventory(
                    resultData,
                    new Extra() { id = enemyId, stackable = false },
                    1,
                    enemyTier,
                    rewardContext.GrantedRewards
                );
            }
        }

        private static void GrantExtraReward(
            Extra extra,
            RewardContext rewardContext,
            ResultData resultData
        )
        {
            int randomChance = _random.Next(1, 101);
            int amount = extra.amount == 0 ? 1 : extra.amount;
            int chance =
                extra.chance == 0
                    ? rewardContext.DifficultyChanceBoost
                    : (int)(extra.chance * rewardContext.ChanceBoostScaler);

            if ((extra.firstTime && rewardContext.FirstClear) || randomChance <= chance)
            {
                int tier = 0; // Default tier for materials/stackable items
                AddToInventory(resultData, extra, amount, tier, rewardContext.GrantedRewards);
            }
        }

        private static void AddToInventory(
            ResultData resultData,
            Extra extra,
            int amount,
            int tier,
            Reward grantedRewards
        )
        {
            string id = extra.id;
            bool isStackable = extra.stackable;
            string stackId = isStackable ? null : Guid.NewGuid().ToString();
            
            // Determine if this item needs display properties (characters, weapons, etc.)
            bool needsDisplayProperties = !isStackable;
            
            var operation = new InventoryOperation
            {
                Add = new AddInventoryItemsOperation
                {
                    Item = new InventoryItemReference { Id = id, StackId = stackId },
                    Amount = amount,
                    NewStackValues = needsDisplayProperties
                        ? new InitialValues { DisplayProperties = new { tier } }
                        : null
                }
            };
            
            resultData.InventoryOperations.Add(operation);
            
            resultData.InventoryItems.Add(
                new InventoryItem
                {
                    Id = id,
                    StackId = stackId,
                    Amount = amount,
                    DisplayProperties = needsDisplayProperties ? new { tier } : null
                }
            );
            
            grantedRewards.extra.Add(new Extra { id = id, amount = amount });
        }

        private static async Task UpdatePlayerData(PlayFabUtil playfabUtil, ResultData resultData)
        {
            // Update player statistics
            if (resultData.StatsUpdate.Any())
            {
                var updateStatReq = new UpdatePlayerStatisticsRequest
                {
                    PlayFabId = playfabUtil.PlayFabId,
                    Statistics = resultData.StatsUpdate
                };
                await playfabUtil.ServerApi.UpdatePlayerStatisticsAsync(updateStatReq);
            }

            // Update user data (EnergyData, HuntingHouseProgression, etc.)
            if (resultData.UserDataRecord.Any())
            {
                var updateUserDataRequest = new UpdateUserDataRequest
                {
                    PlayFabId = playfabUtil.PlayFabId,
                    Data = resultData.UserDataRecord.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Value
                    )
                };
                await playfabUtil.ServerApi.UpdateUserDataAsync(updateUserDataRequest);
            }

            // Update inventory
            if (resultData.InventoryOperations.Any())
            {
                var executeInventoryOperationsRequest = new ExecuteInventoryOperationsRequest
                {
                    Entity = playfabUtil.Entity,
                    Operations = resultData.InventoryOperations,
                    CollectionId = "default"
                };
                await playfabUtil.EconomyApi.ExecuteInventoryOperationsAsync(
                    executeInventoryOperationsRequest
                );
            }
        }

        private static int GetMissionOrTowerMaxMissionId(
            List<StatisticValue> playerStatistics,
            string missionGrade,
            int scaledMissionId
        )
        {
            if (playerStatistics == null || playerStatistics.Count == 0)
            {
                // First mission ever, allow if it's the starting mission (scaledId == 0)
                if (scaledMissionId == 0)
                {
                    return 0;
                }
                throw new Exception("Player can't do this mission - no progression data");
            }

            var stat = playerStatistics.Find(s => s.StatisticName == missionGrade);
            if (stat == null)
            {
                // No progress in this mission grade yet
                if (scaledMissionId == 0)
                {
                    return 0;
                }
                throw new Exception($"Player can't do this mission - no progression in {missionGrade}");
            }

            int maxMissionId = stat.Value;
            if (maxMissionId < scaledMissionId)
            {
                throw new Exception($"Player can't do this mission. Current progress: {maxMissionId}, Required: {scaledMissionId}");
            }

            return maxMissionId;
        }

        private static int GetHuntingHouseMaxBossFloor(
            Dictionary<string, UserDataRecord> userData,
            int missionGradeId,
            int missionId,
            int floorId
        )
        {
            if (userData.ContainsKey("HuntingHouseProgression"))
            {
                var huntingHouseData = JsonConvert.DeserializeObject<
                    Dictionary<string, Dictionary<int, int>>
                >(
                    userData["HuntingHouseProgression"].Value,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );
                string missionGrade = "missiongrade" + missionGradeId;
                if (huntingHouseData.ContainsKey(missionGrade))
                {
                    int maxFloorId = huntingHouseData[missionGrade].GetValueOrDefault(missionId, 0);
                    if (floorId > maxFloorId)
                    {
                        throw new Exception("Player can't do this mission");
                    }
                    return maxFloorId;
                }
            }
            return 0;
        }

        private static void ValidateEnergy(
            Dictionary<string, UserDataRecord> userData,
            ResultData resultData,
            MissionContext missionContext
        )
        {
            if (!userData.ContainsKey("EnergyData"))
            {
                throw new Exception("Energy data not found");
            }

            // 1. Get current energy data
            var energyData = JsonConvert.DeserializeObject<EnergyData>(
                userData["EnergyData"].Value,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );

            // 2. Calculate required energy first
            int requiredEnergy = GetEnergyCost(missionContext);

            // 3. Restore energy before checking requirements
            energyData = EnergySystem.RestoreEnergy(energyData, DateTime.UtcNow);

            // 4. Validate if we have enough energy after restoration
            if (energyData.currentEnergy < requiredEnergy)
            {
                int minutesToWait =
                    (requiredEnergy - energyData.currentEnergy)
                    * EnergySystem.ENERGY_RESTORE_MINUTES;
                throw new Exception(
                    $"Insufficient energy. Required: {requiredEnergy}, Current: {energyData.currentEnergy}, Minutes until enough: {minutesToWait}"
                );
            }

            // 5. Deduct energy cost and update timestamp
            energyData.currentEnergy -= requiredEnergy;

            // 6. Save updated energy data
            resultData.UserDataRecord["EnergyData"] = new UserDataRecord
            {
                Value = JsonConvert.SerializeObject(energyData)
            };
        }

        public static int GetEnergyCost(MissionContext missionContext)
        {
            if (missionContext.IsHuntingHouse)
            {
                return missionContext.FloorId switch
                {
                    <= 2 => MissionRewardConfig.HUNTING_HOUSE_FLOOR_0_2_ENERGY,
                    <= 5 => MissionRewardConfig.HUNTING_HOUSE_FLOOR_3_5_ENERGY,
                    <= 7 => MissionRewardConfig.HUNTING_HOUSE_FLOOR_6_7_ENERGY,
                    _ => MissionRewardConfig.HUNTING_HOUSE_FLOOR_8_PLUS_ENERGY
                };
            }
            else if (missionContext.IsMission)
            {
                return missionContext.Difficulty switch
                {
                    0 => MissionRewardConfig.VILLAGE_DIFFICULTY_0_ENERGY,
                    1 => MissionRewardConfig.VILLAGE_DIFFICULTY_1_ENERGY,
                    _ => MissionRewardConfig.VILLAGE_DIFFICULTY_2_ENERGY
                };
            }
            else if (missionContext.IsTower)
            {
                return MissionRewardConfig.TOWER_ENERGY;
            }
            
            return 0;
        }
    }
}
