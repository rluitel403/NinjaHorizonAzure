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
            var serverApi = new PlayFabServerInstanceAPI(apiSettings, titleContext);
            var economyApi = new PlayFabEconomyInstanceAPI(apiSettings, titleContext);
            var args = context.FunctionArgument;
            int missionGradeId = args.mission_grade_id;
            int missionId = args.mission_id;

            string missionGrade = "missiongrade" + missionGradeId;

            var combinedInfoResult = await serverApi.GetPlayerCombinedInfoAsync(new GetPlayerCombinedInfoRequest()
            {
                PlayFabId = context.CallerEntityProfile.Lineage.MasterPlayerAccountId,
                InfoRequestParameters = new GetPlayerCombinedInfoRequestParams()
                {
                    GetPlayerStatistics = true,
                    PlayerStatisticNames = new List<string> { missionGrade },
                    GetTitleData = true,
                    TitleDataKeys = new List<string> { "missions" }
                }
            });
            var missionGrades = combinedInfoResult.Result.InfoResultPayload.TitleData["missions"];
            var playerStatistics = combinedInfoResult.Result.InfoResultPayload.PlayerStatistics;

            int maxMissionId = 0;
            //validate player can do this mission
            if (playerStatistics.Count == 1)
            {
                maxMissionId = playerStatistics.Find(stat => stat.StatisticName == missionGrade).Value;
                if (maxMissionId < missionId)
                {
                    return new { error = true, message = "You need to unlock the first mission" };
                }
            }

            List<string> selectedCharacters = JsonConvert.DeserializeObject<List<string>>(
                args.selectedCharacters.ToString(),
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
            GetInventoryItemsRequest getInventoryItemsRequest = new GetInventoryItemsRequest()
            {
                Entity = new PlayFab.EconomyModels.EntityKey()
                {
                    Id = context.CallerEntityProfile.Entity.Id,
                    Type = context.CallerEntityProfile.Entity.Type,
                },
                CollectionId = "default",
                Filter = filter
            };

            var inventory = await economyApi.GetInventoryItemsAsync(getInventoryItemsRequest);
            var items = inventory.Result.Items;
            //validate player has selected characters
            if (items.Count != selectedCharacters.Count)
            {
                return new { error = true };
            }

            //parse mission data
            List<MissionGrade> missionGradesList = JsonConvert.DeserializeObject<List<MissionGrade>>(
                missionGrades.ToString(),
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );
            MissionGrade missionGradeData = missionGradesList.Find(mg => mg.mission_grade_id == missionGradeId);
            int rewardXp = missionGradeData.missions[missionId].rewards.xp;
            int rewardGold = missionGradeData.missions[missionId].rewards.gold;
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

            List<Extra> extraRewards = missionGradeData.missions[missionId].rewards.extra ?? new List<Extra>();
            var firstClear = maxMissionId == missionId;
            //mission progression
            if (firstClear)
            { //extra reward
                int numberOfMission = missionGradeData.missions.Count - 1;
                //unlock next mission grade if first time clearing final mission id
                if (missionId == numberOfMission)
                {
                    statsUpdate.Add(
                                       new StatisticUpdate()
                                       { //for mission clear
                                           StatisticName = missionGrade,
                                           Value = 1
                                       }
                                   );
                    statsUpdate.Add(
                        new StatisticUpdate()
                        { //for next mission
                            StatisticName = "missiongrade" + (missionGradeId + 1),
                            Value = 0
                        }
                    );
                }
                else
                {
                    statsUpdate.Add(
                                        new StatisticUpdate()
                                        { //for mission clear
                                            StatisticName = missionGrade,
                                            Value = 1
                                        }
                                    );
                }
            }

            foreach (var extra in extraRewards)
            {
                int randomNumber = new Random().Next(1, 101);
                int amount = extra.amount == 0 ? 1 : extra.amount;
                if ((extra.firstTime && firstClear) || randomNumber <= extra.chance || (extra.chance == 0 && !extra.firstTime))
                {
                    string stackId = Guid.NewGuid().ToString();
                    inventoryOperations.Add(
                        new InventoryOperation()
                        {
                            Add = new AddInventoryItemsOperation()
                            {
                                Item = new InventoryItemReference()
                                {
                                    Id = extra.id,
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
                            Id = extra.id,
                            StackId = stackId,
                            DisplayProperties = new EntityData(),
                            Amount = amount
                        }
                    );
                }
            }

            //gold inventory item
            string goldId = "56afe66a-5a09-4b2d-9f39-3482c39c5779";
            inventoryOperations.Add(
                            new InventoryOperation()
                            {
                                Add = new AddInventoryItemsOperation()
                                {
                                    Item = new InventoryItemReference()
                                    {
                                        Id = goldId,
                                    },
                                    Amount = rewardGold
                                }
                            }
                        );
            inventoryItems.Add(
                new InventoryItem()
                {
                    Id = goldId,
                    Amount = rewardGold
                }
            );

            UpdatePlayerStatisticsRequest updateStatReq = new UpdatePlayerStatisticsRequest()
            {
                PlayFabId = context.CallerEntityProfile.Lineage.MasterPlayerAccountId,
                Statistics = statsUpdate
            };
            await serverApi.UpdatePlayerStatisticsAsync(updateStatReq);
            ExecuteInventoryOperationsRequest executeInventoryOperationsRequest =
                new ExecuteInventoryOperationsRequest()
                {
                    Entity = new PlayFab.EconomyModels.EntityKey()
                    {
                        Id = context.CallerEntityProfile.Entity.Id,
                        Type = context.CallerEntityProfile.Entity.Type,
                    },
                    Operations = inventoryOperations,
                    CollectionId = "default"
                };
            await economyApi.ExecuteInventoryOperationsAsync(executeInventoryOperationsRequest);
            var res = JsonConvert.SerializeObject(new { inventoryItems, statsUpdate, error = false });
            return res;
        }
    }
}
