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
    public static class UpgradeItem
    {
        [FunctionName("UpgradeItem")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            var args = context.FunctionArgument;

            var upgradeCosts = await InventoryUtil.GetUpgradeCosts(playfabUtil);

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
            TierRequirement costItems = upgradeCosts.itemUpgradeCost.Find(x =>
                x.tier == itemData.tier
            );

            CostItem materialCostItem = costItems.requirements.Find(x =>
                !InventoryUtil.IsCurrency(x.itemId)
            );
            CostItem goldOrTokenCostItem = costItems.requirements.Find(x =>
                InventoryUtil.IsCurrency(x.itemId)
            );

            string requiredItemFilter =
                "id eq '"
                + materialCostItem.itemId
                + "'"
                + " or id eq '"
                + goldOrTokenCostItem.itemId
                + "'";

            var getRequiredItemResponse = await playfabUtil.GetInventoryItems(requiredItemFilter);
            var requiredItem = getRequiredItemResponse.Items;
            if (requiredItem.Count != 2)
            {
                throw new Exception("User does not have the required item");
            }
            InventoryItem materialItem = requiredItem.Find(item =>
                item.Id == materialCostItem.itemId
            );
            InventoryItem currencyItem = requiredItem.Find(item =>
                item.Id == goldOrTokenCostItem.itemId
            );

            if (
                materialItem.Amount < materialCostItem.amount
                || currencyItem.Amount < goldOrTokenCostItem.amount
            )
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
                Id = materialCostItem.itemId,
                Amount = materialItem.Amount - materialCostItem.amount
            };

            var updateCurrency = new InventoryItem()
            {
                Id = goldOrTokenCostItem.itemId,
                Amount = currencyItem.Amount - goldOrTokenCostItem.amount
            };

            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>
            {
                new InventoryOperation
                {
                    Update = new UpdateInventoryItemsOperation { Item = updateUpgradeItem }
                },
                new InventoryOperation
                {
                    Update = new UpdateInventoryItemsOperation { Item = updateMaterial }
                },
                new InventoryOperation
                {
                    Update = new UpdateInventoryItemsOperation { Item = updateCurrency }
                }
            };

            await playfabUtil.ExecuteInventoryOperations(inventoryOperations);
            updateMaterial.Amount = -materialCostItem.amount; //ui adds values for currency/materials so we negate for materials. for items, it updates.
            updateCurrency.Amount = -goldOrTokenCostItem.amount;

            return JsonConvert.SerializeObject(
                new
                {
                    inventoryItems = new List<InventoryItem>()
                    {
                        updateMaterial,
                        updateCurrency,
                        updateUpgradeItem
                    }
                }
            );
        }
    }
}
