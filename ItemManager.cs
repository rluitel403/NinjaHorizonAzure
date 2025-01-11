using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            var args = context.FunctionArgument;

            string characterId = args.characterId;
            string itemId = args.itemId;
            string itemType = args.itemType;
            string action = args.action;

            string filter = "stackId eq '" + characterId + "' or stackId eq '" + itemId + "'";
            var inventory = await playfabUtil.GetInventoryItems(filter);
            var items = inventory.Items;

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

            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>
            {
                new InventoryOperation
                {
                    Update = new UpdateInventoryItemsOperation
                    {
                        Item = updateItem
                    }
                }
            };

            await playfabUtil.ExecuteInventoryOperations(inventoryOperations);

            return JsonConvert.SerializeObject(
                new { inventoryItems = new List<InventoryItem>() { updateItem } }
            );
        }
    }
}
