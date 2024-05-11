using System;
using System.Collections.Generic;
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

    public class PlayFabApiUtil
    {
        public PlayFabServerInstanceAPI serverApi { get; set; }
        public PlayFabEconomyInstanceAPI economyApi { get; set; }

        public EntityKey Entity { get; set; }

        public string PlayFabId { get; set; }
    }

    public static class MissionCleared
    {
        [FunctionName("MissionCleared")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            FunctionExecutionContext<dynamic> context = JsonConvert.DeserializeObject<
                FunctionExecutionContext<dynamic>
            >(await req.ReadAsStringAsync());

            var apiSettings = new PlayFabApiSettings()
            {
                TitleId = context.TitleAuthenticationContext.Id,
                DeveloperSecretKey = Environment.GetEnvironmentVariable("DeveloperSecretKey"),
            };

            PlayFabAuthenticationContext titleContext = new PlayFabAuthenticationContext
            {
                EntityToken = context.TitleAuthenticationContext.EntityToken
            };

            var playfabUtil = new PlayFabApiUtil()
            {
                serverApi = new PlayFabServerInstanceAPI(apiSettings, titleContext),
                economyApi = new PlayFabEconomyInstanceAPI(apiSettings, titleContext),
                Entity = new EntityKey()
                {
                    Id = context.CallerEntityProfile.Entity.Id,
                    Type = context.CallerEntityProfile.Entity.Type
                },
                PlayFabId = context.CallerEntityProfile.Lineage.MasterPlayerAccountId
            };

            var args = context.FunctionArgument;
            int missionGradeId = args.mission_grade_id;
            int missionId = args.mission_id;
            int difficulty = args.difficulty;
            int floorId = args.floor_id;
            string pveType = args.pve_type;
            bool isPvE = pveType == "PvE" ? true : false;

            string missionGrade = "missiongrade" + missionGradeId;

            var combinedInfoResult = await playfabUtil.serverApi.GetPlayerCombinedInfoAsync(
                new GetPlayerCombinedInfoRequest()
                {
                    PlayFabId = playfabUtil.PlayFabId,
                    InfoRequestParameters = new GetPlayerCombinedInfoRequestParams()
                    {
                        GetPlayerStatistics = isPvE,
                        PlayerStatisticNames = new List<string> { missionGrade },
                        GetTitleData = true,
                        TitleDataKeys = new List<string> { "missions" },
                        GetUserData = true,
                        UserDataKeys = new List<string>
                        {
                            "HuntingHouseProgression",
                            "SelectedCharacters"
                        }
                    }
                }
            );
            var missionGrades = combinedInfoResult.Result.InfoResultPayload.TitleData["missions"];
            var playerStatistics = combinedInfoResult.Result.InfoResultPayload.PlayerStatistics;
            var userData = combinedInfoResult.Result.InfoResultPayload.UserData;
            int maxMissionId = 0;
            int scaledMissionId = missionId + 10 * difficulty;
            if (isPvE)
            {
                maxMissionId = getVillageMissionMaxMissionId(
                    playerStatistics,
                    missionGrade,
                    scaledMissionId
                );
            }
            else
            {
                maxMissionId = getHuntingHouseMaxBossFloor(
                    userData,
                    missionGradeId,
                    missionId,
                    floorId
                );
            }

            List<string> selectedCharacters = JsonConvert.DeserializeObject<List<string>>(
                userData.GetValueOrDefault("SelectedCharacters").Value,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );
            string filter = "";
            for (int i = 0; i < selectedCharacters.Count; i++)
            {
                if (i != 0)
                {
                    filter += " or stackId eq '" + selectedCharacters[i] + "'";
                }
                else
                {
                    filter += "stackId eq '" + selectedCharacters[i] + "'";
                }
            }

            List<StatisticUpdate> statsUpdate = new List<StatisticUpdate>();
            List<InventoryItem> inventoryItems = new List<InventoryItem>();
            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>();
            Dictionary<string, UserDataRecord> userDataRecord =
                new Dictionary<string, UserDataRecord>();
            GetInventoryItemsRequest getInventoryItemsRequest = new GetInventoryItemsRequest()
            {
                Entity = playfabUtil.Entity,
                CollectionId = "default",
                Filter = filter
            };

            var inventory = await playfabUtil.economyApi.GetInventoryItemsAsync(
                getInventoryItemsRequest
            );
            var items = inventory.Result.Items;
            //validate player has selected characters
            if (items.Count != selectedCharacters.Count)
            {
                throw new Exception("Selected characters not found in inventory");
            }

            //parse mission data
            List<MissionGrade> missionGradesList = JsonConvert.DeserializeObject<
                List<MissionGrade>
            >(
                missionGrades.ToString(),
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );
            MissionGrade missionGradeData = missionGradesList.Find(mg =>
                mg.mission_grade_id == missionGradeId
            );
            Mission mission = missionGradeData.missions[missionId];
            List<string> enemies = mission.enemies;
            Reward rewards = mission.rewards;
            int rewardXp = rewards.xp;
            int rewardGold = rewards.gold;

            float rewardScaler = isPvE ? (1 + (difficulty * 2 / 10f)) : (1 + (floorId / 18f));
            int difficultyChanceBoost = isPvE ? Math.Max(1, difficulty * 2) : 30 + (floorId + 1) * 7;
            float chanceBoostScaler = isPvE ? (1 + (difficulty * 1 / 2f)) : (1 + (floorId / 9f));
            Reward grantedRewards = new Reward
            {
                extra = new List<Extra>(),
                xp = (int)(rewardXp * rewardScaler),
                gold = (int)(rewardGold * rewardScaler)
            };
            grantPlayerXpAndGold(statsUpdate, inventoryItems, inventoryOperations, items, grantedRewards);

            var firstClear = isPvE ? maxMissionId == scaledMissionId : floorId == maxMissionId;
            await updateMissionProgress(
                playfabUtil,
                missionGradeId,
                missionId,
                floorId,
                difficulty,
                isPvE,
                userData,
                statsUpdate,
                userDataRecord,
                firstClear
            );

            grantExtraRewards(inventoryItems, inventoryOperations, rewards, grantedRewards, enemies, isPvE, firstClear, difficultyChanceBoost, chanceBoostScaler);

            await updateInventoryAndStats(playfabUtil, statsUpdate, inventoryOperations);
            var res = JsonConvert.SerializeObject(
                new
                {
                    inventoryItems,
                    statsUpdate,
                    userDataRecord,
                    grantedRewards
                }
            );
            return res;
        }

        private static void grantPlayerXpAndGold(
            List<StatisticUpdate> statsUpdate,
            List<InventoryItem> inventoryItems,
            List<InventoryOperation> inventoryOperations,
            List<InventoryItem> items,
            Reward grantedRewards
        )
        {
            int rewardXp = grantedRewards.xp;
            int rewardGold = grantedRewards.gold;
            //give the characters xp
            foreach (var item in items)
            {
                EntityData entityData = JsonConvert.DeserializeObject<EntityData>(
                    item.DisplayProperties.ToString(),
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );
                entityData.xp = entityData.xp + rewardXp;
                var updateItem = new InventoryItem()
                {
                    Id = item.Id,
                    StackId = item.StackId,
                    DisplayProperties = entityData,
                    Amount = item.Amount
                };
                inventoryOperations.Add(
                    new InventoryOperation()
                    {
                        Update = new UpdateInventoryItemsOperation() { Item = updateItem }
                    }
                );
                inventoryItems.Add(updateItem);
            }
            statsUpdate.Add(
                new StatisticUpdate()
                { //for player
                    StatisticName = "xp",
                    Value = rewardXp
                }
            );
            //gold inventory item
            string goldId = "56afe66a-5a09-4b2d-9f39-3482c39c5779";
            inventoryOperations.Add(
                new InventoryOperation()
                {
                    Add = new AddInventoryItemsOperation()
                    {
                        Item = new InventoryItemReference() { Id = goldId, },
                        Amount = rewardGold
                    }
                }
            );
            inventoryItems.Add(new InventoryItem() { Id = goldId, Amount = rewardGold });
        }

        private static void grantExtraRewards(
            List<InventoryItem> inventoryItems,
            List<InventoryOperation> inventoryOperations,
            Reward rewards,
            Reward grantedRewards,
            List<string> enemies,
            bool isPvE,
            bool firstClear,
            int difficultyChanceBoost,
            float chanceBoostScaler
        )
        {
            if (isPvE)
            {
                int randomNumber = new Random().Next(1, 101);
                if (randomNumber <= difficultyChanceBoost)
                {
                    string enemyId = enemies[new Random().Next(0, enemies.Count)];
                    addToInventory(inventoryItems, inventoryOperations, enemyId, 1, grantedRewards);
                }
            }
            List<Extra> extraRewards = rewards.extra ?? new List<Extra>();

            foreach (var extra in extraRewards)
            {
                int randomNumber = new Random().Next(1, 101);
                int amount = extra.amount == 0 ? 1 : extra.amount;
                int chance = extra.chance == 0 ? extra.chance + difficultyChanceBoost : (int)(extra.chance * chanceBoostScaler);
                if (
                    (extra.firstTime && firstClear)
                    || randomNumber <= chance
                )
                {
                    addToInventory(inventoryItems, inventoryOperations, extra.id, amount, grantedRewards);
                }
            }
        }

        private static void addToInventory(List<InventoryItem> inventoryItems,
            List<InventoryOperation> inventoryOperations, string id, int amount, Reward grantedRewards)
        {
            string stackId = Guid.NewGuid().ToString();
            inventoryOperations.Add(
                new InventoryOperation()
                {
                    Add = new AddInventoryItemsOperation()
                    {
                        Item = new InventoryItemReference()
                        {
                            Id = id,
                            StackId = stackId,
                        },
                        NewStackValues = new InitialValues()
                        {
                            DisplayProperties = new EntityData()
                        },
                        Amount = amount
                    }
                }
            );
            inventoryItems.Add(
                new InventoryItem()
                {
                    Id = id,
                    StackId = stackId,
                    DisplayProperties = new EntityData(),
                    Amount = amount
                }
            );
            grantedRewards.extra.Add(new Extra() { id = id, amount = 1 });
        }

        private static async Task updateMissionProgress(
            PlayFabApiUtil playfabUtil,
            int missionGradeId,
            int missionId,
            int floorId,
            int difficulty,
            bool isPvE,
            Dictionary<string, UserDataRecord> userData,
            List<StatisticUpdate> statsUpdate,
            Dictionary<string, UserDataRecord> userDataRecord,
            bool firstClear
        )
        {
            string missionGrade = "missiongrade" + missionGradeId;
            //mission progression
            if (firstClear)
            { //extra reward
                if (isPvE)
                {
                    int numberOfMission = 9;
                    //unlock next mission grade if first time clearing final mission id
                    if (missionId == numberOfMission && difficulty == 0)
                    {
                        statsUpdate.Add(
                            new StatisticUpdate()
                            { //for next mission
                                StatisticName = "missiongrade" + (missionGradeId + 1),
                                Value = 0
                            }
                        );
                    }

                    statsUpdate.Add(
                        new StatisticUpdate()
                        { //for mission clear
                            StatisticName = missionGrade,
                            Value = 1
                        }
                    );
                }
                //hunting house
                else
                {
                    var huntingHouseData = new Dictionary<string, Dictionary<int, int>>();
                    if (userData.ContainsKey("HuntingHouseProgression"))
                    {
                        huntingHouseData = JsonConvert.DeserializeObject<
                            Dictionary<string, Dictionary<int, int>>
                        >(
                            userData["HuntingHouseProgression"].Value,
                            new JsonSerializerSettings
                            {
                                NullValueHandling = NullValueHandling.Ignore
                            }
                        );
                        if (huntingHouseData.ContainsKey(missionGrade))
                        {
                            int currentMaxFloorId = huntingHouseData[missionGrade]
                                .GetValueOrDefault(missionId, 0);
                            huntingHouseData[missionGrade][missionId] = Math.Max(
                                currentMaxFloorId,
                                floorId + 1
                            );
                        }
                        else
                        {
                            huntingHouseData[missionGrade] = new Dictionary<int, int>();
                            huntingHouseData[missionGrade][missionId] = 1;
                        }
                    }
                    else
                    {
                        huntingHouseData[missionGrade] = new Dictionary<int, int>();
                        huntingHouseData[missionGrade][missionId] = 1;
                    }
                    var huntingHouseProgressionSerialized = JsonConvert.SerializeObject(
                        huntingHouseData
                    );
                    userDataRecord.Add(
                        "HuntingHouseProgression",
                        new UserDataRecord() { Value = huntingHouseProgressionSerialized }
                    );
                    UpdateUserDataRequest updateUserDataRequest = new UpdateUserDataRequest()
                    {
                        Data = new Dictionary<string, string>()
                        {
                            { "HuntingHouseProgression", huntingHouseProgressionSerialized }
                        },
                        PlayFabId = playfabUtil.PlayFabId
                    };
                    await playfabUtil.serverApi.UpdateUserDataAsync(updateUserDataRequest);
                }
            }
        }

        private static async Task updateInventoryAndStats(
            PlayFabApiUtil playFabUtil,
            List<StatisticUpdate> statsUpdate,
            List<InventoryOperation> inventoryOperations
        )
        {
            UpdatePlayerStatisticsRequest updateStatReq = new UpdatePlayerStatisticsRequest()
            {
                PlayFabId = playFabUtil.PlayFabId,
                Statistics = statsUpdate
            };
            await playFabUtil.serverApi.UpdatePlayerStatisticsAsync(updateStatReq);
            ExecuteInventoryOperationsRequest executeInventoryOperationsRequest =
                new ExecuteInventoryOperationsRequest()
                {
                    Entity = playFabUtil.Entity,
                    Operations = inventoryOperations,
                    CollectionId = "default"
                };
            await playFabUtil.economyApi.ExecuteInventoryOperationsAsync(
                executeInventoryOperationsRequest
            );
        }

        private static int getHuntingHouseMaxBossFloor(
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

        private static int getVillageMissionMaxMissionId(
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
    }
}
