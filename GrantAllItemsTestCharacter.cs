using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using PlayFab.EconomyModels;
using System.Linq;

namespace NinjaHorizon.Function
{
    public static class GrantAllItemsTestCharacter
    {
        [FunctionName("GrantAllItemsTestCharacter")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            try
            {
                var context = await PlayFabUtil.ParseFunctionContext(req);
                var playFabUtil = PlayFabUtil.InitializeFromContext(context);

                // Get all catalog items using SearchItems with pagination
                var allCatalogItems = new List<CatalogItem>();
                var allowedTypes = new HashSet<string> { "Entity", "Item", "Currency", "Material", "Skill" };
                string catalogContinuationToken = "";

                while (catalogContinuationToken != null)
                {
                    var searchResult = await playFabUtil.SearchItems("", "", 50, catalogContinuationToken);

                    if (searchResult.Items != null && searchResult.Items.Count > 0)
                    {
                        // Filter items to only include allowed types
                        var filteredItems = searchResult.Items.Where(item =>
                            allowedTypes.Contains(item.ContentType)).ToList();

                        allCatalogItems.AddRange(filteredItems);
                        log.LogInformation($"Retrieved {searchResult.Items.Count} items, filtered to {filteredItems.Count} allowed types. Total so far: {allCatalogItems.Count}");
                    }

                    catalogContinuationToken = searchResult.ContinuationToken;
                }

                log.LogInformation($"Found {allCatalogItems.Count} total catalog items");

                // Create inventory operations to grant all items in batches of 50
                var allGrantedItems = new List<InventoryItem>();
                int batchSize = 50;
                int totalBatches = (int)Math.Ceiling((double)allCatalogItems.Count / batchSize);

                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    var inventoryOperations = new List<InventoryOperation>();
                    var batchGrantedItems = new List<InventoryItem>();

                    int startIndex = batchIndex * batchSize;
                    int endIndex = Math.Min(startIndex + batchSize, allCatalogItems.Count);

                    for (int i = startIndex; i < endIndex; i++)
                    {
                        var catalogItem = allCatalogItems[i];

                        // Check if item is a currency (coin, token, gold)
                        bool isCurrency = catalogItem.Id == InventoryUtil.GOLD_ID ||
                                         catalogItem.Id == InventoryUtil.TOKEN_ID ||
                                         catalogItem.Id == InventoryUtil.COIN_ID;

                        // Generate unique stack ID for non-currency items only
                        string stackId = isCurrency ? null : Guid.NewGuid().ToString();
                        int amount = isCurrency ? 100000000 : 1; // 100M for currencies, 1 for other items

                        // Create the inventory operation to add the item
                        inventoryOperations.Add(new InventoryOperation
                        {
                            Add = new AddInventoryItemsOperation
                            {
                                Item = new InventoryItemReference
                                {
                                    Id = catalogItem.Id,
                                    StackId = stackId
                                },
                                Amount = amount
                            }
                        });

                        // Track the granted item for response
                        batchGrantedItems.Add(new InventoryItem
                        {
                            Id = catalogItem.Id,
                            StackId = stackId,
                            Amount = amount
                        });
                    }

                    // Execute this batch of inventory operations
                    if (inventoryOperations.Count > 0)
                    {
                        await playFabUtil.ExecuteInventoryOperations(inventoryOperations);
                        allGrantedItems.AddRange(batchGrantedItems);
                        log.LogInformation($"Successfully granted batch {batchIndex + 1}/{totalBatches} with {inventoryOperations.Count} items. Total granted: {allGrantedItems.Count}");
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    message = $"Successfully granted {allGrantedItems.Count} items",
                });
            }
            catch (Exception ex)
            {
                log.LogError($"Error in GrantAllItemsTestCharacter: {ex.Message}");
                return new BadRequestObjectResult(JsonConvert.SerializeObject(new
                {
                    error = ex.Message
                }));
            }
        }
    }
}
