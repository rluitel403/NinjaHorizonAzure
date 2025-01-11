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

namespace NinjaHorizon.Function
{
    public class TurnData
    {
        public BattleReplayData battleReplayData { get; set; }
        public PlayerSharedGroupData playerData { get; set; }
        public PlayerSharedGroupData enemyData { get; set; }
    }

    public class BattleReplayData
    {
        public string performedById { get; set; }
        public bool played { get; set; } //was the battle played
        public string sourceStackId { get; set; } //entity who performs the action
        public string targetStackId { get; set; } //entity who is the target of the action
        public int abilityIndex { get; set; } //index of the ability in the entity's ability list
        public int dodgeProbability { get; set; } //dodge chance
        public float damage { get; set; } //damage dealt
    }


    public class PvPCharacterInfo
    {
        public string stackId { get; set; }
        public string itemId { get; set; }
        public EntityData entityData { get; set; }
        public Dictionary<string, Tier> itemsTier { get; set; }
    }

    public class PvPSelectCharacterInput
    {
        public PvPCharacterInfo selectedCharacter { get; set; }
    }

    public class PlayerSharedGroupData
    {
        public bool myTurn { get; set; }

        public string entityCharacterTurnStackId { get; set; } //which characracter from my turn

        public List<PvPCharacterInfo> selectedCharacters { get; set; }

        public bool isReady { get; set; }
    }

    public static class PvP
    {
        public static void GetPlayerEquippedItems() { }

        [FunctionName("PvP")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);

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
            var multiplayerApi = new PlayFabMultiplayerInstanceAPI(apiSettings, titleContext);
            string entityId = context.CallerEntityProfile.Entity.Id;
            string actionType = context.FunctionArgument.actionType;
            string matchId = context.FunctionArgument.matchId;

            switch (actionType)
            {
                case "CreateSharedGroup":
                    {
                        var createSharedGroupRequest = new CreateSharedGroupRequest
                        {
                            SharedGroupId = matchId
                        };
                        PlayFabResult<CreateSharedGroupResult> createSharedGroupResult =
                            await serverApi.CreateSharedGroupAsync(createSharedGroupRequest);
                        //if there is an error and its not due to the shared group already existing, return error
                        if (createSharedGroupResult.Error != null)
                        {
                            if (createSharedGroupResult.Error.ErrorMessage != "InvalidSharedGroupId")
                            {
                                return new
                                {
                                    error = true,
                                    message = createSharedGroupResult.Error.ErrorMessage
                                };
                            }
                            else
                            {
                                return new { error = false, message = "Shared group already exists" };
                            }
                        }
                        var getMatchResult = await multiplayerApi.GetMatchAsync(
                            new PlayFab.MultiplayerModels.GetMatchRequest
                            {
                                QueueName = "PvP",
                                MatchId = matchId
                            }
                        );
                        int randomNumber = new Random().Next(0, 2);

                        var updateSharedGroupDataRequest = new UpdateSharedGroupDataRequest
                        {
                            SharedGroupId = matchId,
                            Data = new Dictionary<string, string>
                        {
                            {
                                entityId = getMatchResult.Result.Members[randomNumber].Entity.Id,
                                JsonConvert.SerializeObject(
                                    new PlayerSharedGroupData() { myTurn = true }
                                )
                            },
                            {
                                entityId = getMatchResult
                                    .Result
                                    .Members[(randomNumber + 1) % 2]
                                    .Entity
                                    .Id,
                                JsonConvert.SerializeObject(
                                    new PlayerSharedGroupData() { myTurn = false }
                                )
                            }
                        }
                        };
                        await serverApi.UpdateSharedGroupDataAsync(updateSharedGroupDataRequest);
                        return new { error = false, message = "Shared group created", };
                    }
                case "SelectCharacter":
                    {
                        PvPSelectCharacterInput pvpInput =
                            JsonConvert.DeserializeObject<PvPSelectCharacterInput>(
                                context.FunctionArgument.pvpSelectCharacterInput.ToString()
                            );
                        var getSharedGroupDataRequest = new GetSharedGroupDataRequest
                        {
                            SharedGroupId = matchId,
                        };
                        PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                            await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);

                        string enemyEntityId = getSharedGroupDataResult
                            .Result.Data.Where(enemy => enemy.Key != entityId)
                            .First()
                            .Key;
                        PlayerSharedGroupData enemySharedGroupInput =
                            JsonConvert.DeserializeObject<PlayerSharedGroupData>(
                                getSharedGroupDataResult.Result.Data[enemyEntityId].Value
                            );
                        enemySharedGroupInput.myTurn = true;

                        PlayerSharedGroupData mySharedGroupInput =
                            JsonConvert.DeserializeObject<PlayerSharedGroupData>(
                                getSharedGroupDataResult.Result.Data[entityId].Value
                            );
                        if (mySharedGroupInput.selectedCharacters == null)
                        {
                            mySharedGroupInput.selectedCharacters = new List<PvPCharacterInfo>();
                        }
                        //get selected characters data
                        GetInventoryItemsRequest getSelectCharacterInventoryRequest =
                            new GetInventoryItemsRequest()
                            {
                                Entity = new PlayFab.EconomyModels.EntityKey()
                                {
                                    Id = context.CallerEntityProfile.Entity.Id,
                                    Type = context.CallerEntityProfile.Entity.Type,
                                },
                                CollectionId = "default",
                                Filter = "stackId eq '" + pvpInput.selectedCharacter.stackId + "'"
                            };
                        var selectCharacterInventory = await economyApi.GetInventoryItemsAsync(
                            getSelectCharacterInventoryRequest
                        );
                        EntityData entityData = JsonConvert.DeserializeObject<EntityData>(
                            selectCharacterInventory.Result.Items[0].DisplayProperties.ToString(),
                            new JsonSerializerSettings
                            {
                                NullValueHandling = NullValueHandling.Ignore,
                                MissingMemberHandling = MissingMemberHandling.Ignore
                            }
                        );
                        pvpInput.selectedCharacter.entityData = entityData;
                        string itemFilter = "";
                        Dictionary<string, Tier> itemsTier = new Dictionary<string, Tier>();
                        if (entityData.weapon != null)
                        {
                            itemFilter += "stackId eq '" + entityData.weapon + "' or ";
                        }
                        if (entityData.backItem != null)
                        {
                            itemFilter += "stackId eq '" + entityData.backItem + "' or ";
                        }
                        if (entityData.clothing != null)
                        {
                            itemFilter += "stackId eq '" + entityData.clothing + "' or ";
                        }
                        if (entityData.artifact != null)
                        {
                            itemFilter += "stackId eq '" + entityData.artifact + "' or ";
                        }
                        itemFilter =
                            itemFilter.Length != 0
                                ? itemFilter.Substring(0, itemFilter.Length - 4)
                                : itemFilter;
                        if (itemFilter.Length != 0)
                        {
                            GetInventoryItemsRequest getEquippedItemsRequest =
                                new GetInventoryItemsRequest()
                                {
                                    Entity = new PlayFab.EconomyModels.EntityKey()
                                    {
                                        Id = context.CallerEntityProfile.Entity.Id,
                                        Type = context.CallerEntityProfile.Entity.Type,
                                    },
                                    CollectionId = "default",
                                    Filter = itemFilter
                                };
                            PlayFabResult<GetInventoryItemsResponse> equippedItems =
                                await economyApi.GetInventoryItemsAsync(getEquippedItemsRequest);
                            foreach (InventoryItem item in equippedItems.Result.Items)
                            {
                                Tier itemData = JsonConvert.DeserializeObject<Tier>(
                                    item.DisplayProperties.ToString()
                                );
                                itemsTier.Add(item.Id, itemData);
                            }
                        }
                        pvpInput.selectedCharacter.itemsTier = itemsTier;
                        mySharedGroupInput.selectedCharacters.Add(pvpInput.selectedCharacter);
                        mySharedGroupInput.myTurn = false;

                        //check if both sides have selected their characters and assign to random player character for turn start
                        if (
                            mySharedGroupInput.selectedCharacters.Count == 3
                            && enemySharedGroupInput.selectedCharacters.Count == 3
                        )
                        {
                            int randomNumber = new Random().Next(0, 2);
                            if (randomNumber == 0)
                            {
                                mySharedGroupInput.myTurn = true;
                                enemySharedGroupInput.myTurn = false;
                                int randomCharacter = new Random().Next(
                                    0,
                                    mySharedGroupInput.selectedCharacters.Count
                                );
                                mySharedGroupInput.entityCharacterTurnStackId = mySharedGroupInput
                                    .selectedCharacters[randomCharacter]
                                    .stackId;
                            }
                            else
                            {
                                enemySharedGroupInput.myTurn = true;
                                mySharedGroupInput.myTurn = false;
                                int randomCharacter = new Random().Next(
                                    0,
                                    enemySharedGroupInput.selectedCharacters.Count
                                );
                                enemySharedGroupInput.entityCharacterTurnStackId = enemySharedGroupInput
                                    .selectedCharacters[randomCharacter]
                                    .stackId;
                            }
                            mySharedGroupInput.isReady = true;
                            enemySharedGroupInput.isReady = true;
                        }

                        var updateSharedGroupDataRequest = new UpdateSharedGroupDataRequest
                        {
                            SharedGroupId = matchId,
                            Data = new Dictionary<string, string>
                        {
                            { entityId, JsonConvert.SerializeObject(mySharedGroupInput) },
                            { enemyEntityId, JsonConvert.SerializeObject(enemySharedGroupInput) }
                        }
                        };

                        await serverApi.UpdateSharedGroupDataAsync(updateSharedGroupDataRequest);
                        return new
                        {
                            error = false,
                            message = "Shared group character selection updated"
                        };
                    }
                case "GetSharedGroupData":
                    {
                        var getSharedGroupDataRequest = new GetSharedGroupDataRequest
                        {
                            SharedGroupId = matchId,
                        };
                        PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                            await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);

                        return new
                        {
                            error = false,
                            message = "Shared group data retrieved",
                            data = getSharedGroupDataResult.Result.Data,
                        };
                    }
                case "UpdateTurnData":
                    {
                        TurnData turnData = JsonConvert.DeserializeObject<TurnData>(
                            context.FunctionArgument.turnData.ToString()
                        );

                        var getSharedGroupDataRequest = new GetSharedGroupDataRequest
                        {
                            SharedGroupId = matchId,
                        };
                        PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                            await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);

                        string enemyEntityId = getSharedGroupDataResult
                            .Result.Data.Where(enemy => enemy.Key != entityId)
                            .First()
                            .Key;
                        PlayerSharedGroupData enemySharedGroup =
                            JsonConvert.DeserializeObject<PlayerSharedGroupData>(
                                getSharedGroupDataResult.Result.Data[enemyEntityId].Value
                            );
                        PlayerSharedGroupData mySharedGroup =
                            JsonConvert.DeserializeObject<PlayerSharedGroupData>(
                                getSharedGroupDataResult.Result.Data[entityId].Value
                            );
                        if (!getSharedGroupDataResult.Result.Data.ContainsKey("BattleReplayData"))
                        {
                            turnData.battleReplayData.dodgeProbability = 50;
                        }
                        else
                        {
                            BattleReplayData battleReplayData =
                                JsonConvert.DeserializeObject<BattleReplayData>(
                                    getSharedGroupDataResult.Result.Data["BattleReplayData"].Value
                                );
                            if (
                                turnData.battleReplayData.dodgeProbability
                                != battleReplayData.dodgeProbability
                            )
                            {
                                throw new Exception("Dodge probability mismatch");
                            }
                            if (turnData.battleReplayData.played)
                            {
                                turnData.battleReplayData.dodgeProbability = new Random().Next(1, 101);
                            }
                        }

                        int randomNumber = new Random().Next(0, 2);
                        if (randomNumber == 0)
                        {
                            int randomCharacter = new Random().Next(
                                0,
                                mySharedGroup.selectedCharacters.Count
                            );
                            mySharedGroup.myTurn = true;
                            enemySharedGroup.myTurn = false;
                            mySharedGroup.entityCharacterTurnStackId = mySharedGroup
                                .selectedCharacters[randomCharacter]
                                .stackId;
                            mySharedGroup.selectedCharacters = turnData.playerData.selectedCharacters;
                        }
                        else
                        {
                            int randomCharacter = new Random().Next(
                                0,
                                enemySharedGroup.selectedCharacters.Count
                            );
                            enemySharedGroup.myTurn = true;
                            mySharedGroup.myTurn = false;
                            enemySharedGroup.entityCharacterTurnStackId = enemySharedGroup
                                .selectedCharacters[randomCharacter]
                                .stackId;
                            enemySharedGroup.selectedCharacters = turnData.enemyData.selectedCharacters;
                        }
                        await serverApi.UpdateSharedGroupDataAsync(
                            new UpdateSharedGroupDataRequest
                            {
                                SharedGroupId = matchId,
                                Data = new Dictionary<string, string>
                                {
                                { entityId, JsonConvert.SerializeObject(mySharedGroup) },
                                { enemyEntityId, JsonConvert.SerializeObject(enemySharedGroup) },
                                {
                                    "BattleReplayData",
                                    JsonConvert.SerializeObject(turnData.battleReplayData)
                                }
                                }
                            }
                        );
                        return mySharedGroup;
                    }
                case "GetTurnData":
                    {
                        var getSharedGroupDataRequest = new GetSharedGroupDataRequest
                        {
                            SharedGroupId = matchId,
                            Keys = new List<string> { "BattleReplayData", entityId }
                        };
                        PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                            await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);
                        return new
                        {
                            error = false,
                            message = "Shared group data retrieved",
                            data = getSharedGroupDataResult.Result.Data,
                        };
                    }
            }
            return new { };
        }
    }
}
