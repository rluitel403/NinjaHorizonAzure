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

namespace Battle.Function
{
    public class TitleAuthenticationContext
    {
        public string Id { get; set; }
        public string EntityToken { get; set; }
    }

    public class FunctionExecutionContext<T>
    {
        public PlayFab.ProfilesModels.EntityProfileBody CallerEntityProfile { get; set; }
        public TitleAuthenticationContext TitleAuthenticationContext { get; set; }
        public bool? GeneratePlayStreamEvent { get; set; }
        public T FunctionArgument { get; set; }
    }

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
    }

    public class Reward
    {
        public int gold { get; set; }
        public int xp { get; set; }

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
        public string MissionGrade { get; set; }
        public int ScaledMissionId { get; set; }
        public int NumberOfMissionOrTower { get; set; }

        public MissionContext(dynamic args)
        {
            MissionGradeId = args.missionGradeId;
            MissionId = args.missionId;
            Difficulty = args.difficulty;
            FloorId = args.floorId;
            IsMissionOrTower = args.battleType == BattleType.PVE || args.battleType == BattleType.PVE_TOWER;
            IsMission = args.battleType == BattleType.PVE;
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
        public int NumberOfMissionOrTower { get; set; }
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
        public List<InventoryOperation> InventoryOperations { get; set; } = new List<InventoryOperation>();
        public Dictionary<string, UserDataRecord> UserDataRecord { get; set; } = new Dictionary<string, UserDataRecord>();
    }

    public class PlayFabApiUtil
    {
        public PlayFabServerInstanceAPI ServerApi { get; set; }
        public PlayFabEconomyInstanceAPI EconomyApi { get; set; }
        public EntityKey Entity { get; set; }
        public string PlayFabId { get; set; }
    }


    public static class MissionCleared
    {
        [FunctionName("MissionCleared")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var context = await ParseFunctionContext(req);
            var playfabUtil = InitializePlayFabUtil(context);
            var args = context.FunctionArgument;

            var missionContext = new MissionContext(args);
            var playerInfo = await GetPlayerInfo(playfabUtil, missionContext);

            var missionData = ParseMissionData(playerInfo.TitleData["missions"], missionContext);
            ValidatePlayerCharacters(playerInfo.Inventory, playerInfo.SelectedCharacters);

            var rewardContext = CalculateRewards(missionContext, missionData, playerInfo.MaxMissionId);
            var resultData = new ResultData();

            await UpdateMissionProgress(playfabUtil, missionContext, playerInfo, rewardContext, resultData);
            GrantRewards(playfabUtil, missionContext, missionData, rewardContext, playerInfo.Inventory, resultData);
            await UpdateInventoryAndStats(playfabUtil, resultData);

            return JsonConvert.SerializeObject(new
            {
                inventoryItems = resultData.InventoryItems,
                statsUpdate = resultData.StatsUpdate,
                userDataRecord = resultData.UserDataRecord,
                grantedRewards = rewardContext.GrantedRewards
            });
        }

        private static async Task<FunctionExecutionContext<dynamic>> ParseFunctionContext(HttpRequest req)
        {
            return JsonConvert.DeserializeObject<FunctionExecutionContext<dynamic>>(await req.ReadAsStringAsync());
        }

        private static PlayFabApiUtil InitializePlayFabUtil(FunctionExecutionContext<dynamic> context)
        {
            var apiSettings = new PlayFabApiSettings
            {
                TitleId = context.TitleAuthenticationContext.Id,
                DeveloperSecretKey = Environment.GetEnvironmentVariable("DeveloperSecretKey"),
            };

            var titleContext = new PlayFabAuthenticationContext
            {
                EntityToken = context.TitleAuthenticationContext.EntityToken
            };

            return new PlayFabApiUtil
            {
                ServerApi = new PlayFabServerInstanceAPI(apiSettings, titleContext),
                EconomyApi = new PlayFabEconomyInstanceAPI(apiSettings, titleContext),
                Entity = new EntityKey
                {
                    Id = context.CallerEntityProfile.Entity.Id,
                    Type = context.CallerEntityProfile.Entity.Type
                },
                PlayFabId = context.CallerEntityProfile.Lineage.MasterPlayerAccountId
            };
        }

        private static async Task<PlayerInfo> GetPlayerInfo(PlayFabApiUtil playfabUtil, MissionContext missionContext)
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
                        UserDataKeys = new List<string> { "HuntingHouseProgression", "SelectedCharacters" }
                    }
                }
            );

            var playerInfo = new PlayerInfo
            {
                TitleData = combinedInfoResult.Result.InfoResultPayload.TitleData,
                PlayerStatistics = combinedInfoResult.Result.InfoResultPayload.PlayerStatistics,
                UserData = combinedInfoResult.Result.InfoResultPayload.UserData,
                SelectedCharacters = JsonConvert.DeserializeObject<List<string>>(
                    combinedInfoResult.Result.InfoResultPayload.UserData["SelectedCharacters"].Value,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                )
            };

            playerInfo.MaxMissionId = missionContext.IsMissionOrTower
                ? GetVillageMissionMaxMissionId(playerInfo.PlayerStatistics, missionContext.MissionGrade, missionContext.ScaledMissionId)
                : GetHuntingHouseMaxBossFloor(playerInfo.UserData, missionContext.MissionGradeId, missionContext.MissionId, missionContext.FloorId);

            playerInfo.Inventory = await GetPlayerInventory(playfabUtil, playerInfo.SelectedCharacters);

            return playerInfo;
        }

        private static async Task<List<InventoryItem>> GetPlayerInventory(PlayFabApiUtil playfabUtil, List<string> selectedCharacters)
        {
            string filter = string.Join(" or ", selectedCharacters.Select(c => $"stackId eq '{c}'"));

            var getInventoryItemsRequest = new GetInventoryItemsRequest
            {
                Entity = playfabUtil.Entity,
                CollectionId = "default",
                Filter = filter
            };

            var inventory = await playfabUtil.EconomyApi.GetInventoryItemsAsync(getInventoryItemsRequest);
            return inventory.Result.Items;
        }

        private static MissionData ParseMissionData(string missionGrades, MissionContext missionContext)
        {
            var missionGradesList = JsonConvert.DeserializeObject<List<MissionGrade>>(
                missionGrades,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );

            var missionGradeData = missionGradesList.Find(mg => mg.mission_grade_id == missionContext.MissionGradeId);
            var mission = missionGradeData.missions[missionContext.MissionId];

            return new MissionData
            {
                Enemies = mission.enemies,
                Rewards = mission.rewards,
                NumberOfMissionOrTower = missionGradeData.missions.Count - 1
            };
        }

        private static void ValidatePlayerCharacters(List<InventoryItem> inventory, List<string> selectedCharacters)
        {
            if (inventory.Count != selectedCharacters.Count)
            {
                throw new Exception("Selected characters not found in inventory");
            }
        }

        private static RewardContext CalculateRewards(MissionContext missionContext, MissionData missionData, int maxMissionId)
        {
            float rewardScaler = missionContext.IsMissionOrTower
                ? (1 + (missionContext.Difficulty * 2 / 10f))
                : (1 + (missionContext.FloorId / 18f));

            int difficultyChanceBoost = missionContext.IsMissionOrTower
                ? Math.Max(1, missionContext.Difficulty * 2)
                : 30 + (missionContext.FloorId + 1) * 7;

            float chanceBoostScaler = missionContext.IsMissionOrTower
                ? (1 + (missionContext.Difficulty * 1 / 2f))
                : (1 + (missionContext.FloorId / 9f));

            return new RewardContext
            {
                GrantedRewards = new Reward
                {
                    extra = new List<Extra>(),
                    xp = (int)(missionData.Rewards.xp * rewardScaler),
                    gold = (int)(missionData.Rewards.gold * rewardScaler)
                },
                DifficultyChanceBoost = difficultyChanceBoost,
                ChanceBoostScaler = chanceBoostScaler,
                FirstClear = missionContext.IsMissionOrTower
                    ? maxMissionId == missionContext.ScaledMissionId
                    : missionContext.FloorId == maxMissionId
            };
        }

        private static async Task UpdateMissionProgress(
            PlayFabApiUtil playfabUtil,
            MissionContext missionContext,
            PlayerInfo playerInfo,
            RewardContext rewardContext,
            ResultData resultData)
        {
            if (rewardContext.FirstClear)
            {
                if (missionContext.IsMissionOrTower)
                {
                    UpdateVillageMissionProgress(missionContext, resultData);
                }
                else
                {
                    await UpdateHuntingHouseProgress(playfabUtil, missionContext, playerInfo, resultData);
                }
            }
        }

        private static void UpdateVillageMissionProgress(MissionContext missionContext, ResultData resultData)
        {
            if (missionContext.MissionId == missionContext.NumberOfMissionOrTower && missionContext.Difficulty == 0)
            {
                resultData.StatsUpdate.Add(new StatisticUpdate
                {
                    StatisticName = $"missiongrade{missionContext.MissionGradeId + 1}",
                    Value = 0
                });
            }

            resultData.StatsUpdate.Add(new StatisticUpdate
            {
                StatisticName = missionContext.MissionGrade,
                Value = 1
            });
        }

        private static async Task UpdateHuntingHouseProgress(
            PlayFabApiUtil playfabUtil,
            MissionContext missionContext,
            PlayerInfo playerInfo,
            ResultData resultData)
        {
            var huntingHouseData = new Dictionary<string, Dictionary<int, int>>();
            if (playerInfo.UserData.ContainsKey("HuntingHouseProgression"))
            {
                huntingHouseData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, int>>>(
                    playerInfo.UserData["HuntingHouseProgression"].Value,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );
            }

            if (!huntingHouseData.ContainsKey(missionContext.MissionGrade))
            {
                huntingHouseData[missionContext.MissionGrade] = new Dictionary<int, int>();
            }

            int currentMaxFloorId = huntingHouseData[missionContext.MissionGrade].GetValueOrDefault(missionContext.MissionId, 0);
            huntingHouseData[missionContext.MissionGrade][missionContext.MissionId] = Math.Max(currentMaxFloorId, missionContext.FloorId + 1);

            var huntingHouseProgressionSerialized = JsonConvert.SerializeObject(huntingHouseData);
            resultData.UserDataRecord["HuntingHouseProgression"] = new UserDataRecord { Value = huntingHouseProgressionSerialized };

            await playfabUtil.ServerApi.UpdateUserDataAsync(new UpdateUserDataRequest
            {
                Data = new Dictionary<string, string> { { "HuntingHouseProgression", huntingHouseProgressionSerialized } },
                PlayFabId = playfabUtil.PlayFabId
            });
        }

        private static void GrantRewards(
            PlayFabApiUtil playfabUtil,
            MissionContext missionContext,
            MissionData missionData,
            RewardContext rewardContext,
            List<InventoryItem> inventory,
            ResultData resultData)
        {
            GrantPlayerXpAndGold(inventory, rewardContext.GrantedRewards, resultData);
            GrantExtraRewards(missionContext, missionData, rewardContext, resultData);
        }

        private static void GrantPlayerXpAndGold(List<InventoryItem> inventory, Reward grantedRewards, ResultData resultData)
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
                resultData.InventoryOperations.Add(new InventoryOperation
                {
                    Update = new UpdateInventoryItemsOperation { Item = updateItem }
                });
                resultData.InventoryItems.Add(updateItem);
            }

            resultData.StatsUpdate.Add(new StatisticUpdate
            {
                StatisticName = "xp",
                Value = grantedRewards.xp
            });

            string goldId = "b4a8100a-73a4-47e5-85e1-23a56a66a313";
            resultData.InventoryOperations.Add(new InventoryOperation
            {
                Add = new AddInventoryItemsOperation
                {
                    Item = new InventoryItemReference { Id = goldId },
                    Amount = grantedRewards.gold
                }
            });
            resultData.InventoryItems.Add(new InventoryItem { Id = goldId, Amount = grantedRewards.gold });
        }

        private static void GrantExtraRewards(
            MissionContext missionContext,
            MissionData missionData,
            RewardContext rewardContext,
            ResultData resultData)
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

        private static void GrantMissionEnemyReward(MissionData missionData, RewardContext rewardContext, ResultData resultData)
        {
            int randomNumber = new Random().Next(1, 101);
            if (randomNumber <= rewardContext.DifficultyChanceBoost)
            {
                string enemyId = missionData.Enemies[new Random().Next(0, missionData.Enemies.Count)];
                AddToInventory(resultData, enemyId, 1, rewardContext.GrantedRewards);
            }
        }

        private static void GrantExtraReward(Extra extra, RewardContext rewardContext, ResultData resultData)
        {
            int randomNumber = new Random().Next(1, 101);
            int amount = extra.amount == 0 ? 1 : extra.amount;
            int chance = extra.chance == 0 ? rewardContext.DifficultyChanceBoost : (int)(extra.chance * rewardContext.ChanceBoostScaler);

            if ((extra.firstTime && rewardContext.FirstClear) || randomNumber <= chance)
            {
                AddToInventory(resultData, extra.id, amount, rewardContext.GrantedRewards);
            }
        }

        private static void AddToInventory(ResultData resultData, string id, int amount, Reward grantedRewards)
        {
            string stackId = Guid.NewGuid().ToString();
            resultData.InventoryOperations.Add(new InventoryOperation
            {
                Add = new AddInventoryItemsOperation
                {
                    Item = new InventoryItemReference
                    {
                        Id = id,
                        StackId = stackId,
                    },
                    // NewStackValues = new InitialValues
                    // {
                    //     DisplayProperties = new EntityData()
                    // },
                    Amount = amount
                }
            });
            resultData.InventoryItems.Add(new InventoryItem
            {
                Id = id,
                StackId = stackId,
                // DisplayProperties = new EntityData(),
                Amount = amount
            });
            grantedRewards.extra.Add(new Extra { id = id, amount = amount });
        }

        private static async Task UpdateInventoryAndStats(PlayFabApiUtil playfabUtil, ResultData resultData)
        {
            var updateStatReq = new UpdatePlayerStatisticsRequest
            {
                PlayFabId = playfabUtil.PlayFabId,
                Statistics = resultData.StatsUpdate
            };
            await playfabUtil.ServerApi.UpdatePlayerStatisticsAsync(updateStatReq);

            var executeInventoryOperationsRequest = new ExecuteInventoryOperationsRequest
            {
                Entity = playfabUtil.Entity,
                Operations = resultData.InventoryOperations,
                CollectionId = "default"
            };
            await playfabUtil.EconomyApi.ExecuteInventoryOperationsAsync(executeInventoryOperationsRequest);
        }

        private static int GetVillageMissionMaxMissionId(List<StatisticValue> playerStatistics, string missionGrade, int scaledMissionId)
        {
            if (playerStatistics.Count == 1)
            {
                int maxMissionId = playerStatistics.Find(stat => stat.StatisticName == missionGrade).Value;
                if (maxMissionId < scaledMissionId)
                {
                    throw new Exception("Player can't do this mission");
                }
                return maxMissionId;
            }
            return 0;
        }

        private static int GetHuntingHouseMaxBossFloor(Dictionary<string, UserDataRecord> userData, int missionGradeId, int missionId, int floorId)
        {
            if (userData.ContainsKey("HuntingHouseProgression"))
            {
                var huntingHouseData = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, int>>>(
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
    }
}
