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

namespace NinjaHorizon.Function
{

    public static class ItemManager
    {
        [FunctionName("ItemManager")]
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

            string characterId = args.characterId;
            string itemId = args.itemId;
            string itemType = args.itemType;
            string action = args.action;

            string filter = "stackId eq '" + characterId + "' or stackId eq '" + itemId + "'";
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
            if (action == "Equip" && items.Count != 2)
            {
                return JsonConvert.SerializeObject(
                    new { invetoryItems = new List<InventoryItem>() { } }
                );
            }
            InventoryItem characterItem = items.Find((item) => item.StackId == characterId);
            EntityData entityData = JsonConvert.DeserializeObject<EntityData>(
                characterItem.DisplayProperties.ToString(),
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );
            itemId = action == "Unequip" ? null : itemId;
            switch (itemType)
            {
                case "Weapon":
                    entityData.weapon = itemId;
                    break;
                case "BackItem":
                    entityData.backItem = itemId;
                    break;
                case "Clothing":
                    entityData.clothing = itemId;
                    break;
                case "Artifact":
                    entityData.artifact = itemId;
                    break;
            }
            var updateItem = new InventoryItem()
            {
                Id = characterItem.Id,
                StackId = characterItem.StackId,
                DisplayProperties = entityData,
                Amount = characterItem.Amount
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
            var result = await economyApi.UpdateInventoryItemsAsync(updateInventoryItemsRequest);
            return JsonConvert.SerializeObject(
                new { inventoryItems = new List<InventoryItem>() { updateItem } }
            );
        }
    }
}
