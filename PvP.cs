using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
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
    public class PvPInput
    {
        public string actionType { get; set; }
        public string matchId { get; set; }
        public List<string> selectedCharacters { get; set; }
    }

    public class PlayerSharedGroupInput
    {
        public bool myTurn { get; set; }
        public List<string> selectedCharacters { get; set; }
    }

    public static class PvP
    {
        [FunctionName("PvP")]
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
            var multiplayerApi = new PlayFabMultiplayerInstanceAPI(apiSettings, titleContext);
            string entityId = context.CallerEntityProfile.Entity.Id;
            PvPInput pvpInput = JsonConvert.DeserializeObject<PvPInput>(
                context.FunctionArgument.ToString()
            );

            switch (pvpInput.actionType)
            {
                case "CreateSharedGroup":
                {
                    var createSharedGroupRequest = new CreateSharedGroupRequest
                    {
                        SharedGroupId = pvpInput.matchId
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
                            MatchId = pvpInput.matchId
                        }
                    );
                    int randomNumber = new Random().Next(0, 2);

                    var updateSharedGroupDataRequest = new UpdateSharedGroupDataRequest
                    {
                        SharedGroupId = pvpInput.matchId,
                        Data = new Dictionary<string, string>
                        {
                            {
                                entityId = getMatchResult.Result.Members[randomNumber].Entity.Id,
                                JsonConvert.SerializeObject(
                                    new PlayerSharedGroupInput() { myTurn = true }
                                )
                            },
                            {
                                entityId = getMatchResult
                                    .Result
                                    .Members[(randomNumber + 1) % 2]
                                    .Entity
                                    .Id,
                                JsonConvert.SerializeObject(
                                    new PlayerSharedGroupInput() { myTurn = false }
                                )
                            }
                        }
                    };
                    await serverApi.UpdateSharedGroupDataAsync(updateSharedGroupDataRequest);
                    return new { error = false, message = "Shared group created", };
                }
                case "SelectCharacter":
                {
                    var getSharedGroupDataRequest = new GetSharedGroupDataRequest
                    {
                        SharedGroupId = pvpInput.matchId,
                    };
                    PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                        await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);

                    string enemyEntityId = getSharedGroupDataResult
                        .Result.Data.Where(enemy => enemy.Key != entityId)
                        .First()
                        .Key;
                    PlayerSharedGroupInput enemySharedGroupInput =
                        JsonConvert.DeserializeObject<PlayerSharedGroupInput>(
                            getSharedGroupDataResult.Result.Data[enemyEntityId].Value
                        );
                    enemySharedGroupInput.myTurn = true;

                    PlayerSharedGroupInput mySharedGroupInput =
                        JsonConvert.DeserializeObject<PlayerSharedGroupInput>(
                            getSharedGroupDataResult.Result.Data[entityId].Value
                        );
                    mySharedGroupInput.selectedCharacters = pvpInput.selectedCharacters;
                    mySharedGroupInput.myTurn = false;

                    var updateSharedGroupDataRequest = new UpdateSharedGroupDataRequest
                    {
                        SharedGroupId = pvpInput.matchId,
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
                        SharedGroupId = pvpInput.matchId,
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
