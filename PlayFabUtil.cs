using System;
using System.Collections.Generic;
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
        public EntityKey Entity { get; private set; }
        public string PlayFabId { get; private set; }

        private PlayFabUtil(
            PlayFabServerInstanceAPI serverApi,
            PlayFabEconomyInstanceAPI economyApi,
            EntityKey entity,
            string playFabId)
        {
            ServerApi = serverApi;
            EconomyApi = economyApi;
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
                entity,
                context.CallerEntityProfile.Lineage.MasterPlayerAccountId
            );
        }

        public static async Task<FunctionExecutionContext<dynamic>> ParseFunctionContext(HttpRequest req)
        {
            string requestBody = await req.ReadAsStringAsync();
            var context = JsonConvert.DeserializeObject<FunctionExecutionContext<dynamic>>(requestBody);
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
                throw new Exception($"Failed to execute inventory operations: {result.Error.ErrorMessage}");
            }
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
            var request = new GetUserDataRequest
            {
                PlayFabId = PlayFabId,
                Keys = keys
            };
            var result = await ServerApi.GetUserDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get user data: {result.Error.ErrorMessage}");
            }
            return result.Result;
        }

        public async Task UpdateUserData(Dictionary<string, string> data)
        {
            var request = new UpdateUserDataRequest
            {
                PlayFabId = PlayFabId,
                Data = data
            };
            var result = await ServerApi.UpdateUserDataAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update user data: {result.Error.ErrorMessage}");
            }
        }
    }
}