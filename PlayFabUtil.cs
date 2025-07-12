using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using PlayFab;
using PlayFab.EconomyModels;
using PlayFab.ServerModels;
using EntityKey = PlayFab.EconomyModels.EntityKey;

namespace NinjaHorizon.Function
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

    public class PlayFabUtil
    {
        public PlayFabServerInstanceAPI ServerApi { get; private set; }
        public PlayFabEconomyInstanceAPI EconomyApi { get; private set; }
        public PlayFabMultiplayerInstanceAPI MultiplayerApi { get; private set; }
        public PlayFabProgressionInstanceAPI ProgressionApi { get; private set; }
        public PlayFabGroupsInstanceAPI GroupsApi { get; private set; }
        public EntityKey Entity { get; private set; }
        public string PlayFabId { get; private set; }

        private PlayFabUtil(
            PlayFabServerInstanceAPI serverApi,
            PlayFabEconomyInstanceAPI economyApi,
            PlayFabMultiplayerInstanceAPI multiplayerApi,
            PlayFabProgressionInstanceAPI progressionApi,
            PlayFabGroupsInstanceAPI groupsApi,
            EntityKey entity,
            string playFabId
        )
        {
            ServerApi = serverApi;
            EconomyApi = economyApi;
            MultiplayerApi = multiplayerApi;
            ProgressionApi = progressionApi;
            GroupsApi = groupsApi;
            Entity = entity;
            PlayFabId = playFabId;
        }

        public static PlayFabUtil InitializeFromContext(FunctionExecutionContext<dynamic> context)
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

            var entity = new EntityKey
            {
                Id = context.CallerEntityProfile.Entity.Id,
                Type = context.CallerEntityProfile.Entity.Type
            };

            return new PlayFabUtil(
                new PlayFabServerInstanceAPI(apiSettings, titleContext),
                new PlayFabEconomyInstanceAPI(apiSettings, titleContext),
                new PlayFabMultiplayerInstanceAPI(apiSettings, titleContext),
                new PlayFabProgressionInstanceAPI(apiSettings, titleContext),
                new PlayFabGroupsInstanceAPI(apiSettings, titleContext),
                entity,
                context.CallerEntityProfile.Lineage.MasterPlayerAccountId
            );
        }

        public static async Task<FunctionExecutionContext<dynamic>> ParseFunctionContext(
            HttpRequest req
        )
        {
            string requestBody = await req.ReadAsStringAsync();
            var context = JsonConvert.DeserializeObject<FunctionExecutionContext<dynamic>>(
                requestBody
            );
            return context;
        }

        public async Task<GetInventoryItemsResponse> GetInventoryItems(string filter)
        {
            var request = new GetInventoryItemsRequest
            {
                Entity = Entity,
                CollectionId = "default",
                Filter = filter
            };

            var result = await EconomyApi.GetInventoryItemsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get inventory items: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<List<InventoryItem>> GetCurrencyItems(List<string> currencyIds)
        {
            var filter = string.Join(" or ", currencyIds.Select(id => $"id eq '{id}'"));
            var inventoryItems = await GetInventoryItems(filter);
            return inventoryItems.Items;
        }

        public async Task<InventoryItem> GetCurrencyItem(
            string currencyId,
            int? expectedAmount = null
        )
        {
            var inventoryItems = await GetCurrencyItems(new List<string> { currencyId });
            if (inventoryItems.Count != 1)
            {
                throw new Exception("User does not have the required item");
            }
            var inventoryItem = inventoryItems[0];
            if (expectedAmount != null && inventoryItem.Amount < expectedAmount)
            {
                throw new Exception("User does not have the required amount");
            }
            return inventoryItem;
        }

        public async Task ExecuteInventoryOperations(List<InventoryOperation> operations)
        {
            var request = new ExecuteInventoryOperationsRequest
            {
                Entity = Entity,
                Operations = operations,
                CollectionId = "default"
            };

            var result = await EconomyApi.ExecuteInventoryOperationsAsync(request);
            if (result.Error != null)
            {
                throw new Exception(
                    $"Failed to execute inventory operations: {result.Error.ErrorMessage}"
                );
            }
        }

        public async Task<SearchItemsResponse> SearchItems(string search = "", string filter = "", int count = 50, string continuationToken = null)
        {
            var request = new SearchItemsRequest
            {
                Entity = Entity,
                Search = search,
                Filter = filter,
                Count = count
            };

            if (!string.IsNullOrEmpty(continuationToken))
            {
                request.ContinuationToken = continuationToken;
            }

            var result = await EconomyApi.SearchItemsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to search items: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<GetTitleDataResult> GetTitleData(List<string> keys)
        {
            var request = new GetTitleDataRequest { Keys = keys };
            var result = await ServerApi.GetTitleDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get title data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<GetUserDataResult> GetUserData(List<string> keys)
        {
            var request = new GetUserDataRequest { PlayFabId = PlayFabId, Keys = keys };
            var result = await ServerApi.GetUserDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get user data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<GetPlayerCombinedInfoResult> GetPlayerCombinedInfo(
            List<string> titleDataKeys,
            List<string> userDataKeys,
            List<string> playerStatisticNames
        )
        {
            var infoRequestParameters = new GetPlayerCombinedInfoRequestParams();

            if (playerStatisticNames != null && playerStatisticNames.Count > 0)
            {
                infoRequestParameters.GetPlayerStatistics = true;
                infoRequestParameters.PlayerStatisticNames = playerStatisticNames;
            }

            if (titleDataKeys != null && titleDataKeys.Count > 0)
            {
                infoRequestParameters.GetTitleData = true;
                infoRequestParameters.TitleDataKeys = titleDataKeys;
            }

            if (userDataKeys != null && userDataKeys.Count > 0)
            {
                infoRequestParameters.GetUserData = true;
                infoRequestParameters.UserDataKeys = userDataKeys;
            }

            var request = new GetPlayerCombinedInfoRequest
            {
                PlayFabId = PlayFabId,
                InfoRequestParameters = infoRequestParameters
            };

            var result = await ServerApi.GetPlayerCombinedInfoAsync(request);
            return result.Result;
        }

        public async Task UpdateUserData(Dictionary<string, string> data)
        {
            var request = new UpdateUserDataRequest { PlayFabId = PlayFabId, Data = data };
            var result = await ServerApi.UpdateUserDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update user data: {result.Error.ErrorMessage}");
            }
        }

        public async Task UpdatePlayerStatistics(Dictionary<string, int> statistics)
        {
            var statisticUpdates = statistics.Select(stat => new StatisticUpdate
            {
                StatisticName = stat.Key,
                Value = stat.Value
            }).ToList();

            var request = new UpdatePlayerStatisticsRequest
            {
                PlayFabId = PlayFabId,
                Statistics = statisticUpdates
            };
            var result = await ServerApi.UpdatePlayerStatisticsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update player statistics: {result.Error.ErrorMessage}");
            }
        }

        public async Task CreateSharedGroup(string sharedGroupId)
        {
            var request = new CreateSharedGroupRequest
            {
                SharedGroupId = sharedGroupId
            };

            await ServerApi.CreateSharedGroupAsync(request);
        }

        public async Task<GetSharedGroupDataResult> GetSharedGroupData(string sharedGroupId)
        {
            var request = new GetSharedGroupDataRequest
            {
                SharedGroupId = sharedGroupId
            };

            var result = await ServerApi.GetSharedGroupDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get shared group data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task<UpdateSharedGroupDataResult> UpdateSharedGroupData(string sharedGroupId, Dictionary<string, string> data)
        {
            var request = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = sharedGroupId,
                Data = data
            };

            var result = await ServerApi.UpdateSharedGroupDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update shared group data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public static string GetStackIdFromType(string type)
        {
            if (type == "Entity" || type == "Weapon" || type == "BackItem" || type == "Clothing")
                return Guid.NewGuid().ToString();
            return null;
        }
    }
}
