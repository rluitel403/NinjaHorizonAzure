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

namespace Inventory.Function
{
    public class Tier
    {
        public int tier { get; set; }

    }
    public static class UpgradeItem
    {
        [FunctionName("UpgradeItem")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
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

            string itemId = args.itemId;
            string filter = "stackId eq '" + itemId + "'";
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
            if (items.Count != 1)
            {
                return JsonConvert.SerializeObject(
                    new { inventoryItems = new List<InventoryItem>() { } }
                );
            }
            var item = items[0];
            Tier itemData = JsonConvert.DeserializeObject<Tier>(
               item.DisplayProperties.ToString(),
               new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
           );
            itemData.tier += 1;
            var updateItem = new InventoryItem()
            {
                Id = item.Id,
                StackId = item.StackId,
                DisplayProperties = itemData,
                Amount = item.Amount
            };
            UpdateInventoryItemsRequest updateInventoryItemsRequest =
                new UpdateInventoryItemsRequest()
                {
                    Item = updateItem,
                    Entity = new PlayFab.EconomyModels.EntityKey()
                    {
                        Id = context.CallerEntityProfile.Entity.Id,
                        Type = context.CallerEntityProfile.Entity.Type,
                    },
                };
            await economyApi.UpdateInventoryItemsAsync(updateInventoryItemsRequest);
            return JsonConvert.SerializeObject(
                new { inventoryItems = new List<InventoryItem>() { updateItem } }
            );
        }
    }
}

