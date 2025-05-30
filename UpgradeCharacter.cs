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
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);
            var playfabUtil = PlayFabUtil.InitializeFromContext(context);
            UpgradeCharacterInput upgradeCharacterInput =
                JsonConvert.DeserializeObject<UpgradeCharacterInput>(
                    context.FunctionArgument.ToString()
                );

            //validate user has all characters
            var filter =
                "stackId eq '"
                + upgradeCharacterInput.characterToEvolveStackId
                + "' or stackId eq '"
                + string.Join(
                    "' or stackId eq '",
                    upgradeCharacterInput.charactersToUseAsFoodStackIds
                )
                + "'";

            var inventory = await playfabUtil.GetInventoryItems(filter);

            InventoryItem characterToEvolveItem = inventory.Items.Find(item =>
                item.StackId == upgradeCharacterInput.characterToEvolveStackId
            );
            EntityData characterToEvolveData = JsonConvert.DeserializeObject<EntityData>(
                characterToEvolveItem.DisplayProperties.ToString()
            );

            var upgradeCosts = await InventoryUtil.GetUpgradeCosts(playfabUtil);

            TierRequirement costItems = upgradeCosts.characterUpgradeCost.Find(x =>
                x.tier == characterToEvolveData.tier
            );

            CostItem goldOrTokenCostItem = costItems.requirements.Find(x =>
                InventoryUtil.IsGoldOrToken(x.itemId)
            );

            //validate sufficient currency
            var ownedCurrencyItem = await playfabUtil.GetCurrencyItem(
                goldOrTokenCostItem.itemId,
                goldOrTokenCostItem.amount
            );

            List<InventoryItemReference> charactersToDeleteItem =
                new List<InventoryItemReference>();

            //all food character must be evolve character tier
            foreach (
                var characterToUseAsFoodStackId in upgradeCharacterInput.charactersToUseAsFoodStackIds
            )
            {
                InventoryItem characterToUseAsFoodItem = inventory.Items.Find(item =>
                    item.StackId == characterToUseAsFoodStackId
                );
                EntityData characterToUseAsFood = JsonConvert.DeserializeObject<EntityData>(
                    characterToUseAsFoodItem.DisplayProperties.ToString()
                );
                if (characterToUseAsFood.tier != characterToEvolveData.tier)
                {
                    throw new Exception("Must unequip all items before evolving");
                }
                //validate character attributes are null or empty string
                if (
                    characterToUseAsFood.weapon != null
                    || characterToUseAsFood.backItem != null
                    || characterToUseAsFood.clothing != null
                    || characterToUseAsFood.artifact != null
                )
                {
                    throw new Exception("Must unequip all items before evolving");
                }
                charactersToDeleteItem.Add(
                    new InventoryItemReference()
                    {
                        Id = characterToUseAsFoodItem.Id,
                        StackId = characterToUseAsFoodItem.StackId,
                    }
                );
            }

            if (
                characterToEvolveData.tier >= 4
                || upgradeCharacterInput.charactersToUseAsFoodStackIds.Count
                    != characterToEvolveData.tier + 1
            )
            {
                throw new Exception("User does not have all characters");
            }

            characterToEvolveData.tier = characterToEvolveData.tier + 1;
            InventoryItem updateCharacterToEvolve = new InventoryItem()
            {
                Id = characterToEvolveItem.Id,
                StackId = characterToEvolveItem.StackId,
                DisplayProperties = characterToEvolveData,
                Amount = 1
            };

            InventoryItem updateCurrency = new InventoryItem()
            {
                Id = goldOrTokenCostItem.itemId,
                Amount = ownedCurrencyItem.Amount - goldOrTokenCostItem.amount
            };

            //change evolve character tier to tier + 1
            //all other food character must be deleted
            List<InventoryOperation> inventoryOperations = new List<InventoryOperation>
            {
                new InventoryOperation()
                {
                    Update = new UpdateInventoryItemsOperation() { Item = updateCharacterToEvolve }
                },
                new InventoryOperation()
                {
                    Update = new UpdateInventoryItemsOperation() { Item = updateCurrency }
                }
            };

            foreach (var characterToDeleteItem in charactersToDeleteItem)
            {
                inventoryOperations.Add(
                    new InventoryOperation()
                    {
                        Delete = new DeleteInventoryItemsOperation()
                        {
                            Item = characterToDeleteItem
                        }
                    }
                );
            }

            await playfabUtil.ExecuteInventoryOperations(inventoryOperations);
            updateCurrency.Amount = -goldOrTokenCostItem.amount;

            //update achievement if first time evolving
            return JsonConvert.SerializeObject(
                new
                {
                    inventoryItemsToAdd = new List<InventoryItem>()
                    {
                        updateCharacterToEvolve,
                        updateCurrency
                    },
                    inventoryItemsToDelete = charactersToDeleteItem
                }
            );
        }
    }
}
