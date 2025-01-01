using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PlayFab;
using PlayFab.EconomyModels;

namespace NinjaHorizon.Function
{
    public class UpgradeCharacterInput
    {
        public string characterToEvolveStackId { get; set; }
        public List<string> charactersToUseAsFoodStackIds { get; set; }
    }
    public static class UpgradeCharacter
    {
        [FunctionName("UpgradeCharacter")]
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
            var economyApi = new PlayFabEconomyInstanceAPI(apiSettings, titleContext);
            UpgradeCharacterInput upgradeCharacterInput = JsonConvert.DeserializeObject<UpgradeCharacterInput>(context.FunctionArgument.ToString());
            //validate user has all characters
            var filter = "stackId eq '" + upgradeCharacterInput.characterToEvolveStackId + "' or stackId eq '" + string.Join("' or stackId eq '", upgradeCharacterInput.charactersToUseAsFoodStackIds) + "'";
            var inventory = await economyApi.GetInventoryItemsAsync(new GetInventoryItemsRequest()
            {
                Entity = new EntityKey()
                {
                    Id = context.CallerEntityProfile.Entity.Id,
                    Type = context.CallerEntityProfile.Entity.Type,
                },
                CollectionId = "default",
                Filter = filter
            });
            InventoryItem characterToEvolveItem = inventory.Result.Items.Find(item => item.StackId == upgradeCharacterInput.characterToEvolveStackId);
            EntityData characterToEvolveData = JsonConvert.DeserializeObject<EntityData>(characterToEvolveItem.DisplayProperties.ToString());
            List<InventoryItemReference> charactersToDeleteItem = new List<InventoryItemReference>();
            //all food character must be evolve character tier
            foreach (var characterToUseAsFoodStackId in upgradeCharacterInput.charactersToUseAsFoodStackIds)
            {
                InventoryItem characterToUseAsFoodItem = inventory.Result.Items.Find(item => item.StackId == characterToUseAsFoodStackId);
                EntityData characterToUseAsFood = JsonConvert.DeserializeObject<EntityData>(characterToUseAsFoodItem.DisplayProperties.ToString());
                if (characterToUseAsFood.tier != characterToEvolveData.tier)
                {
                    throw new Exception("Must unequip all items before evolving");
                }
                //validate character attributes are null or empty string
                if (characterToUseAsFood.weapon != null)
                {
                    throw new Exception("Must unequip all items before evolving");
                }
                if (characterToUseAsFood.backItem != null)
                {
                    throw new Exception("Must unequip all items before evolving");
                }
                if (characterToUseAsFood.clothing != null)
                {
                    throw new Exception("Must unequip all items before evolving");
                }
                if (characterToUseAsFood.artifact != null)
                {
                    throw new Exception("Must unequip all items before evolving");
                }
                charactersToDeleteItem.Add(new InventoryItemReference()
                {
                    Id = characterToUseAsFoodItem.Id,
                    StackId = characterToUseAsFoodItem.StackId,
                });
            }
            if (characterToEvolveData.tier >= 4 || upgradeCharacterInput.charactersToUseAsFoodStackIds.Count != characterToEvolveData.tier + 1)
            {
                throw new Exception("User does not have all characters");
            }

            characterToEvolveData.tier = characterToEvolveData.tier + 1;
            InventoryItem characterToEvolveUpdated = new InventoryItem()
            {
                Id = characterToEvolveItem.Id,
                StackId = characterToEvolveItem.StackId,
                DisplayProperties = characterToEvolveData,
                Amount = 1
            };
            //change evolve character tier to tier + 1
            //all other food character must be deleted
            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>
            {
                new InventoryOperation()
                {
                    Update = new UpdateInventoryItemsOperation()
                    {
                        Item = characterToEvolveUpdated
                    }
                }
            };
            foreach (var characterToDeleteItem in charactersToDeleteItem)
            {
                inventoryOperations.Add(new InventoryOperation()
                {
                    Delete = new DeleteInventoryItemsOperation()
                    {
                        Item = characterToDeleteItem
                    }
                });
            }
            await economyApi.ExecuteInventoryOperationsAsync(new ExecuteInventoryOperationsRequest()
            {
                Entity = new EntityKey()
                {
                    Id = context.CallerEntityProfile.Entity.Id,
                    Type = context.CallerEntityProfile.Entity.Type,
                },
                Operations = inventoryOperations,
                CollectionId = "default"
            });

            return JsonConvert.SerializeObject(
                new
                {
                    inventoryItemsToAdd = new List<InventoryItem>() { characterToEvolveUpdated },
                    inventoryItemsToDelete = charactersToDeleteItem
                }
            );

        }
    }
}

