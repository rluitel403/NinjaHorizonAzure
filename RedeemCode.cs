using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PlayFab.EconomyModels;

namespace NinjaHorizon.Function
{
    public class RedeemCodeItem
    {
        public string itemId { get; set; }
        public int amount { get; set; }
    }

    public static class RedeemCode
    {
        [FunctionName("RedeemCode")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {

            // Parse the function context
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            var args = context.FunctionArgument;

            // Get the redeem code from the request
            string redeemCode = args?.code;
            if (string.IsNullOrEmpty(redeemCode))
            {
                throw new Exception("Redeem code is required");
            }

            // Fetch title data containing redeem codes
            var titleDataResult = await playfabUtil.GetTitleData(new List<string> { "redeemCodes" });
            if (!titleDataResult.Data.ContainsKey("redeemCodes"))
            {
                throw new Exception("Redeem codes not configured");
            }

            // Parse redeem codes data
            var redeemCodesData = JsonConvert.DeserializeObject<Dictionary<string, List<RedeemCodeItem>>>(
                titleDataResult.Data["redeemCodes"].ToString()
            );

            // Find the specific redeem code
            var codeData = redeemCodesData[redeemCode];
            if (codeData == null)
            {
                throw new Exception("Invalid redeem code");
            }

            // Check if code has already been redeemed
            var userDataResult = await playfabUtil.GetUserData(new List<string> { "RedeemedCodes" });
            List<string> redeemedCodes = new List<string>();

            if (userDataResult.Data.ContainsKey("RedeemedCodes"))
            {
                redeemedCodes = JsonConvert.DeserializeObject<List<string>>(
                    userDataResult.Data["RedeemedCodes"].Value
                ) ?? new List<string>();
            }

            if (redeemedCodes.Contains(redeemCode))
            {
                throw new Exception("Code already redeemed");
            }

            // Grant the items
            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>();
            List<InventoryItem> grantedItems = new List<InventoryItem>();

            foreach (var item in codeData)
            {
                // Generate stackId for non-currency items (currency items use null stackId)
                string stackId = InventoryUtil.IsCurrency(item.itemId) ? null : Guid.NewGuid().ToString();

                inventoryOperations.Add(new InventoryOperation
                {
                    Add = new AddInventoryItemsOperation
                    {
                        Amount = item.amount,
                        Item = new InventoryItemReference { Id = item.itemId, StackId = stackId }
                    }
                });

                grantedItems.Add(new InventoryItem
                {
                    Amount = item.amount,
                    Id = item.itemId,
                    StackId = stackId
                });
            }

            // Execute inventory operations to grant items
            await playfabUtil.ExecuteInventoryOperations(inventoryOperations);

            // Mark code as redeemed
            redeemedCodes.Add(redeemCode);
            var updatedUserData = new Dictionary<string, string>
                {
                    { "RedeemedCodes", JsonConvert.SerializeObject(redeemedCodes) }
                };
            await playfabUtil.UpdateUserData(updatedUserData);

            return JsonConvert.SerializeObject(new { grantedItems, redeemedCodes });

        }
    }
}
