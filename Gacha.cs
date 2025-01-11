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
    public class GachaItem
    {
        public string type;
        public string itemId;
        public int amount;
        public int chance;
    }

    public static class Gacha
    {
        [FunctionName("Gacha")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);

            var wheelData = await playfabUtil.GetTitleData(new List<string> { "wheel" });
            List<GachaItem> gachaItems = JsonConvert.DeserializeObject<List<GachaItem>>(
                wheelData.Data["wheel"].ToString()
            );

            string coinItemId = "bf180bae-b805-43c9-99db-e2e8fc1a0719";
            string coinItemFilter = "id eq '" + coinItemId + "'";
            var getCoinResponse = await playfabUtil.GetInventoryItems(coinItemFilter);
            var coinItem = getCoinResponse.Items;
            if (coinItem.Count != 1)
            {
                throw new Exception("User does not have coins");
            }
            var coinAmount = coinItem[0].Amount;
            if (coinAmount < 1)
            {
                throw new Exception("Insufficient amount of coin");
            }

            Dictionary<int, double> items = new Dictionary<int, double>();
            for (int i = 0; i < gachaItems.Count; i++)
            {
                items[i] = gachaItems[i].chance;
            }
            Random rand = new Random();
            double totalWeight = 0;
            foreach (double weight in items.Values)
            {
                totalWeight += weight;
            }
            double randomNumber = rand.NextDouble() * totalWeight;
            double cumulativeWeight = 0;
            int itemIndex = 0;
            foreach (KeyValuePair<int, double> kvp in items)
            {
                cumulativeWeight += kvp.Value;
                if (randomNumber <= cumulativeWeight)
                {
                    itemIndex = kvp.Key;
                    break;
                }
            }
            GachaItem item = gachaItems[itemIndex];
            string itemId = item.itemId;

            string stackId = item.type != "Currency" ? Guid.NewGuid().ToString() : null;

            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>{
                new InventoryOperation
                {
                    Subtract = new SubtractInventoryItemsOperation
                    {
                        Amount = 1,
                        Item = new InventoryItemReference{
                            Id = coinItemId
                        }
                    }
                },
                new InventoryOperation
                {
                    Add = new AddInventoryItemsOperation{
                        Amount = item.amount,
                        Item = new InventoryItemReference()
                        {
                            Id = itemId,
                            StackId = stackId
                        },
                    }
                }
            };

            await playfabUtil.ExecuteInventoryOperations(inventoryOperations);

            List<InventoryItem> inventoryItems = new List<InventoryItem> {
                new InventoryItem() { Amount = item.amount, Id = itemId, StackId = stackId },
                new InventoryItem() { Amount = -1, Id = coinItemId }
            };

            return new { itemIndex, inventoryItems };
        }
    }
}
