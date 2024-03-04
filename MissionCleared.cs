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
        public string xp { get; set; }
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

            int xp = args.reward.xp;
            int gold = args.reward.gold;
            int missionGradeId = args.mission_grade_id;
            int missionId = args.mission_id;
            int maxMissionId = args.max_mission_id;

            string missionGrade = "missiongrade" + missionGradeId;

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
            foreach (var item in items)
            {
                EntityData entityData = JsonConvert.DeserializeObject<EntityData>(
                    item.DisplayProperties.ToString(),
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
                );
                entityData.xp = (int.Parse(entityData.xp) + xp).ToString();
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
                    Value = xp
                }
            );
            if (
                missionGradeId == 0
                && (missionId == 0 || missionId == 1)
                && maxMissionId == missionId
            )
            { //extra reward
                Dictionary<int, List<string>> rewards = new Dictionary<int, List<string>>
                {
                    {
                        0,
                        new List<string>
                        {
                            "614328f3-c4d2-426d-8d37-f7c363bae2a1",
                            "ed371564-7d24-4754-a3e1-1077a4e91c5d"
                        }
                    }
                };
                string stackId = Guid.NewGuid().ToString();
                inventoryOperations.Add(
                    new InventoryOperation()
                    {
                        Add = new AddInventoryItemsOperation()
                        {
                            Item = new InventoryItemReference()
                            {
                                Id = rewards[missionGradeId][missionId],
                                StackId = stackId,
                            },
                            NewStackValues = new InitialValues()
                            {
                                DisplayProperties = new EntityData() { xp = "0" }
                            },
                            Amount = 1
                        }
                    }
                );
                inventoryItems.Add(
                    new InventoryItem()
                    {
                        Id = rewards[missionGradeId][missionId],
                        StackId = stackId,
                        DisplayProperties = new EntityData() { xp = "0" },
                        Amount = 1
                    }
                );
            }
            if (missionGradeId <= 4 && missionId < 9 && maxMissionId == missionId)
            {
                statsUpdate.Add(
                    new StatisticUpdate()
                    { //for mission clear
                        StatisticName = missionGrade,
                        Value = 1
                    }
                );
            }
            else if (missionGradeId <= 4 && missionId == 9 && maxMissionId == missionId)
            {
                statsUpdate.Add(
                    new StatisticUpdate()
                    { //for next mission
                        StatisticName = "missiongrade" + (missionGradeId + 1),
                        Value = 0
                    }
                );
            }

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
            var res = JsonConvert.SerializeObject(new { inventoryItems, statsUpdate });
            return res;
        }
    }
}
