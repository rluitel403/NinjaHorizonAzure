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
        public static string PVE = "PVE";
        public static string PVP = "PVP";
        public static string PVE_HUTNING_HOUSE = "PVE_HUTNING_HOUSE";
        public static string PVE_TOWER = "PVE_TOWER";
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

        public MissionContext(dynamic args)
        {
            MissionGradeId = args.missionGradeId;
            MissionId = args.missionId;
            Difficulty = args.difficulty;
            FloorId = args.floorId;
            IsHuntingHouse = args.battleType == BattleType.PVE_HUTNING_HOUSE;
            IsMissionOrTower =
                args.battleType == BattleType.PVE || args.battleType == BattleType.PVE_TOWER;
            IsMission = args.battleType == BattleType.PVE;
            IsTower = args.battleType == BattleType.PVE_TOWER;
            MissionGrade = "missiongrade" + MissionGradeId;
            ScaledMissionId = MissionId + 10 * Difficulty;
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
        [FunctionName("MissionCleared")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            var args = context.FunctionArgument;

            var missionContext = new MissionContext(args);
            var playerInfo = await GetPlayerInfo(playfabUtil, missionContext);
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
            MissionContext missionContext
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

            var playerInfo = new PlayerInfo
            {
                TitleData = combinedInfoResult.Result.InfoResultPayload.TitleData,
                PlayerStatistics = combinedInfoResult.Result.InfoResultPayload.PlayerStatistics,
                UserData = combinedInfoResult.Result.InfoResultPayload.UserData,
                SelectedCharacters = JsonConvert.DeserializeObject<List<string>>(
                    combinedInfoResult
                        .Result
                        .InfoResultPayload
                        .UserData["SelectedCharacters"]
                        .Value,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                )
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
                    Rewards = new Reward(),
                    NumberOfMission = 0
                };
            }

            var missionGradesList = JsonConvert.DeserializeObject<List<MissionGrade>>(
                missionGrades,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );

            var missionGradeData = missionGradesList.Find(mg =>
                mg.mission_grade_id == missionContext.MissionGradeId
            );
            var mission = missionGradeData.missions[missionContext.MissionId];

            return new MissionData
            {
                Enemies = mission.enemies,
                Rewards = mission.rewards,
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
                float rewardScaler = 1 + (missionContext.Difficulty * 2 / 10f);
                grantedRewards = new Reward
                {
                    extra = new List<Extra>(),
                    xp = (int)(missionData.Rewards.xp * rewardScaler),
                    gold = (int)(missionData.Rewards.gold * rewardScaler),
                    token = 0
                };
                difficultyChanceBoost = Math.Max(1, missionContext.Difficulty * 2);
                chanceBoostScaler = 1 + (missionContext.Difficulty * 1 / 2f);
            }
            else if (missionContext.IsTower)
            {
                grantedRewards = new Reward
                {
                    extra = new List<Extra>(),
                    gold = (missionContext.MissionId + 1) * 100,
                    xp = (missionContext.MissionId + 1) * 100,
                    token = (missionContext.MissionId + 1) % 5 == 0 ? 5 : 0
                };
                difficultyChanceBoost = Math.Max(1, missionContext.Difficulty * 2);
                chanceBoostScaler = 1 + (missionContext.Difficulty * 1 / 2f);
            }
            else
            {
                float rewardScaler = 1 + (missionContext.FloorId / 2);
                float baseXp = 400;
                float baseGold = 400;
                grantedRewards = new Reward
                {
                    extra = new List<Extra>(),
                    xp = (int)(baseXp * rewardScaler),
                    gold = (int)(baseGold * rewardScaler),
                    token = 0
                };
                difficultyChanceBoost = 30 + (missionContext.FloorId + 1) * 7;
                chanceBoostScaler = 1 + (missionContext.FloorId / 9f);
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
            int randomNumber = new Random().Next(1, 101);
            int energyAmountToGrant = 1;
            if (randomNumber <= 30)
            {
                //energy data is in resultData.UserDataRecord since we used it before
                var energyData = JsonConvert.DeserializeObject<EnergyData>(
                    resultData.UserDataRecord["EnergyData"].Value,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );
                energyData.currentEnergy = energyData.currentEnergy + energyAmountToGrant;
                resultData.UserDataRecord["EnergyData"] = new UserDataRecord
                {
                    Value = JsonConvert.SerializeObject(energyData)
                };
                rewardContext.GrantedRewards.energy = energyAmountToGrant;
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
            int randomNumber = new Random().Next(1, 101);
            if (randomNumber <= rewardContext.DifficultyChanceBoost)
            {
                string enemyId = missionData.Enemies[
                    new Random().Next(0, missionData.Enemies.Count)
                ];
                AddToInventory(
                    resultData,
                    new Extra() { id = enemyId, stackable = false },
                    1,
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
            int randomNumber = new Random().Next(1, 101);
            int amount = extra.amount == 0 ? 1 : extra.amount;
            int chance =
                extra.chance == 0
                    ? rewardContext.DifficultyChanceBoost
                    : (int)(extra.chance * rewardContext.ChanceBoostScaler);

            if ((extra.firstTime && rewardContext.FirstClear) || randomNumber <= chance)
            {
                AddToInventory(resultData, extra, amount, rewardContext.GrantedRewards);
            }
        }

        private static void AddToInventory(
            ResultData resultData,
            Extra extra,
            int amount,
            Reward grantedRewards
        )
        {
            string id = extra.id;
            string stackId = extra.stackable ? null : Guid.NewGuid().ToString();
            resultData.InventoryOperations.Add(
                new InventoryOperation
                {
                    Add = new AddInventoryItemsOperation
                    {
                        Item = new InventoryItemReference { Id = id, StackId = stackId, },
                        Amount = amount
                    }
                }
            );
            resultData.InventoryItems.Add(
                new InventoryItem
                {
                    Id = id,
                    StackId = stackId,
                    Amount = amount
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
            if (playerStatistics.Count == 1)
            {
                int maxMissionId = playerStatistics
                    .Find(stat => stat.StatisticName == missionGrade)
                    .Value;
                if (maxMissionId < scaledMissionId)
                {
                    throw new Exception("Player can't do this mission");
                }
                return maxMissionId;
            }
            return 0;
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
                if (missionContext.FloorId <= 2)
                {
                    return 5;
                }
                else if (missionContext.FloorId <= 5)
                {
                    return 6;
                }
                else if (missionContext.FloorId <= 7)
                {
                    return 7;
                }
                return 8;
            }
            else if (missionContext.IsMission)
            {
                if (missionContext.Difficulty == 0)
                {
                    return 3;
                }
                else if (missionContext.Difficulty == 1)
                {
                    return 4;
                }
                return 5;
            }
            else if (missionContext.IsTower)
            {
                return 5;
            }
            return 0;
        }
    }
}
