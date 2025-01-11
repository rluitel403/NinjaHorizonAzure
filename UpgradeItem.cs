using System;
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
    public class UpgradeItemRequirement
    {
        public string requiredItemId;
        public int amount;
    }

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
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            var args = context.FunctionArgument;

            var upgradeItemRequirementData = await playfabUtil.GetTitleData(
                new List<string> { "upgradeItemRequirement" }
            );
            Dictionary<int, UpgradeItemRequirement> upgradeItemRequirements = JsonConvert.DeserializeObject<Dictionary<int, UpgradeItemRequirement>>(
                upgradeItemRequirementData.Data["upgradeItemRequirement"].ToString()
            );

            string itemId = args.itemId;
            string upgradeItemFilter = "stackId eq '" + itemId + "'";
            var getUpgradeItemResult = await playfabUtil.GetInventoryItems(upgradeItemFilter);
            var upgradeItem = getUpgradeItemResult.Items;
            if (upgradeItem.Count != 1)
            {
                throw new Exception("User does not have the required item");
            }

            var item = upgradeItem[0];
            Tier itemData = JsonConvert.DeserializeObject<Tier>(
               item.DisplayProperties.ToString(),
               new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }
            );

            //validate user has sufficient materials to upgrade item
            UpgradeItemRequirement upgradeItemRequirement = upgradeItemRequirements[itemData.tier + 1];
            string requiredItemFilter = "id eq '" + upgradeItemRequirement.requiredItemId + "'";
            var getRequiredItemResponse = await playfabUtil.GetInventoryItems(requiredItemFilter);
            var requiredItem = getRequiredItemResponse.Items;
            if (requiredItem.Count != 1)
            {
                throw new Exception("User does not have the required item");
            }
            if (requiredItem[0].Amount < upgradeItemRequirement.amount)
            {
                throw new Exception("Insufficient amount of item for upgrade");
            }

            itemData.tier += 1;
            var updateUpgradeItem = new InventoryItem()
            {
                Id = item.Id,
                StackId = item.StackId,
                DisplayProperties = itemData,
                Amount = item.Amount
            };
            var updateMaterial = new InventoryItem()
            {
                Id = upgradeItemRequirement.requiredItemId,
                Amount = requiredItem[0].Amount - upgradeItemRequirement.amount
            };

            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>{
                new InventoryOperation
                {
                    Update = new UpdateInventoryItemsOperation
                    {
                        Item = updateUpgradeItem
                    }
                },
                new InventoryOperation
                {
                    Update = new UpdateInventoryItemsOperation
                    {
                        Item = updateMaterial
                    }
                }
            };

            await playfabUtil.ExecuteInventoryOperations(inventoryOperations);
            updateMaterial.Amount = -upgradeItemRequirement.amount; //ui adds values for currency/materials so we negate for materials. for items, it updates.

            return JsonConvert.SerializeObject(
                new
                {
                    inventoryItems = new List<InventoryItem>() { updateMaterial, updateUpgradeItem }
                }
            );
        }
    }
}

