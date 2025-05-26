using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PlayFab;
using PlayFab.EconomyModels;
using PlayFab.ServerModels;

namespace NinjaHorizon.Function
{
    public class UpdateTurnDataInput
    {
        public BattleReplayData battleReplayData { get; set; }
        public PlayerSharedGroupData playerData { get; set; }
        public PlayerSharedGroupData enemyData { get; set; }
    }

    public class BattleReplayData
    {
        public string performedById { get; set; }
        public bool played { get; set; } //was the battle played
        public string sourceStackId { get; set; } //entity who performs the action
        public string targetStackId { get; set; } //entity who is the target of the action
        public int abilityIndex { get; set; } //index of the ability in the entity's ability list
        public float damage { get; set; } //damage dealt
    }


    public class PvPCharacterInfo
    {
        public string stackId { get; set; }
        public string itemId { get; set; }
        public float baseSpeed { get; set; }
        public EntityData entityData { get; set; }
        public Dictionary<string, PvPItemIdAndTier> itemIdAndTier { get; set; }
    }

    public class PvPItemIdAndTier
    {
        public string itemId { get; set; }
        public Tier tier { get; set; }
    }

    public class PvPSelectCharacterInput
    {
        public PvPCharacterInfo selectedCharacter { get; set; }
    }

    public class PlayerSharedGroupData
    {
        public string myEntityId { get; set; }
        public string enemyEntityId { get; set; }
        public bool myTurn { get; set; }

        public string entityCharacterTurnStackId { get; set; } //which characracter from my turn

        public List<PvPCharacterInfo> selectedCharacters { get; set; }

        public bool isReady { get; set; }

        // Turn counter for each character (stackId -> counter value)
        public Dictionary<string, float> turnCounters { get; set; } = new Dictionary<string, float>();
    }

    public static class PvP
    {
        public static void GetPlayerEquippedItems() { }

        private static async Task<dynamic> CreateSharedGroupAsync(
            PlayFabServerInstanceAPI serverApi,
            PlayFabMultiplayerInstanceAPI multiplayerApi,
            string matchId,
            string entityId)
        {
            var createSharedGroupRequest = new CreateSharedGroupRequest
            {
                SharedGroupId = matchId
            };

            PlayFabResult<CreateSharedGroupResult> createSharedGroupResult =
                await serverApi.CreateSharedGroupAsync(createSharedGroupRequest);

            var getMatchResult = await multiplayerApi.GetMatchAsync(
                new PlayFab.MultiplayerModels.GetMatchRequest
                {
                    QueueName = "PvP",
                    MatchId = matchId
                }
            );

            int randomNumber = new Random().Next(0, 2);
            string myEntityId = getMatchResult.Result.Members[randomNumber].Entity.Id;
            string enemyEntityId = getMatchResult.Result.Members[(randomNumber + 1) % 2].Entity.Id;
            var updateSharedGroupDataRequest = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = matchId,
                Data = new Dictionary<string, string>
                {
                    {
                        myEntityId,
                        JsonConvert.SerializeObject(new PlayerSharedGroupData() { myTurn = true, myEntityId = myEntityId, enemyEntityId = enemyEntityId })
                    },
                    {
                        enemyEntityId,
                        JsonConvert.SerializeObject(new PlayerSharedGroupData() { myTurn = false, myEntityId = enemyEntityId, enemyEntityId = myEntityId })
                    }
                }
            };

            await serverApi.UpdateSharedGroupDataAsync(updateSharedGroupDataRequest);
            return new { };
        }

        private static async Task<dynamic> SelectCharacterAsync(
            PlayFabServerInstanceAPI serverApi,
            PlayFabEconomyInstanceAPI economyApi,
            FunctionExecutionContext<dynamic> context,
            string matchId,
            string entityId)
        {
            PvPSelectCharacterInput pvpInput =
                JsonConvert.DeserializeObject<PvPSelectCharacterInput>(
                    context.FunctionArgument.pvpSelectCharacterInput.ToString()
                );

            var getSharedGroupDataRequest = new GetSharedGroupDataRequest
            {
                SharedGroupId = matchId,
            };

            PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);

            PlayerSharedGroupData mySharedGroupInput =
                JsonConvert.DeserializeObject<PlayerSharedGroupData>(
                    getSharedGroupDataResult.Result.Data[entityId].Value
                );
            string enemyEntityId = mySharedGroupInput.enemyEntityId;
            PlayerSharedGroupData enemySharedGroupInput =
                JsonConvert.DeserializeObject<PlayerSharedGroupData>(
                    getSharedGroupDataResult.Result.Data[enemyEntityId].Value
                );
            enemySharedGroupInput.myTurn = true;
            mySharedGroupInput.myTurn = false;
            if (mySharedGroupInput.selectedCharacters == null)
            {
                mySharedGroupInput.selectedCharacters = new List<PvPCharacterInfo>();
            }

            // Get selected character data
            await PopulateCharacterDataAsync(economyApi, context, pvpInput.selectedCharacter);

            mySharedGroupInput.selectedCharacters.Add(pvpInput.selectedCharacter);

            // Check if both sides have selected their characters
            if (mySharedGroupInput.selectedCharacters.Count == 3 &&
                enemySharedGroupInput.selectedCharacters.Count == 3)
            {
                AssignRandomPlayerTurn(mySharedGroupInput, enemySharedGroupInput);
            }

            int seed = new Random().Next();
            var updateSharedGroupDataRequest = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = matchId,
                Data = new Dictionary<string, string>
                {
                    { entityId, JsonConvert.SerializeObject(mySharedGroupInput) },
                    { enemyEntityId, JsonConvert.SerializeObject(enemySharedGroupInput) },
                    { "seed", seed.ToString() }
                }
            };

            await serverApi.UpdateSharedGroupDataAsync(updateSharedGroupDataRequest);
            return new { };
        }

        private static async Task PopulateCharacterDataAsync(
            PlayFabEconomyInstanceAPI economyApi,
            FunctionExecutionContext<dynamic> context,
            PvPCharacterInfo character)
        {
            GetInventoryItemsRequest getSelectCharacterInventoryRequest =
                new GetInventoryItemsRequest()
                {
                    Entity = new PlayFab.EconomyModels.EntityKey()
                    {
                        Id = context.CallerEntityProfile.Entity.Id,
                        Type = context.CallerEntityProfile.Entity.Type,
                    },
                    CollectionId = "default",
                    Filter = "stackId eq '" + character.stackId + "'"
                };

            var selectCharacterInventory = await economyApi.GetInventoryItemsAsync(
                getSelectCharacterInventoryRequest
            );

            EntityData entityData = JsonConvert.DeserializeObject<EntityData>(
                selectCharacterInventory.Result.Items[0].DisplayProperties.ToString(),
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                }
            );
            character.entityData = entityData;

            // Get equipped items data
            string itemFilter = BuildItemFilter(entityData);
            Dictionary<string, PvPItemIdAndTier> itemsTier = new Dictionary<string, PvPItemIdAndTier>();

            if (!string.IsNullOrEmpty(itemFilter))
            {
                itemsTier = await GetEquippedItemsDataAsync(economyApi, context, itemFilter);
            }

            character.itemIdAndTier = itemsTier;
        }

        private static string BuildItemFilter(EntityData entityData)
        {
            var itemIds = new List<string>();

            if (entityData.weapon != null)
                itemIds.Add(entityData.weapon);
            if (entityData.backItem != null)
                itemIds.Add(entityData.backItem);
            if (entityData.clothing != null)
                itemIds.Add(entityData.clothing);
            if (entityData.artifact != null)
                itemIds.Add(entityData.artifact);

            return itemIds.Count > 0
                ? string.Join(" or ", itemIds.Select(id => $"stackId eq '{id}'"))
                : "";
        }

        private static async Task<Dictionary<string, PvPItemIdAndTier>> GetEquippedItemsDataAsync(
            PlayFabEconomyInstanceAPI economyApi,
            FunctionExecutionContext<dynamic> context,
            string itemFilter)
        {
            GetInventoryItemsRequest getEquippedItemsRequest = new GetInventoryItemsRequest()
            {
                Entity = new PlayFab.EconomyModels.EntityKey()
                {
                    Id = context.CallerEntityProfile.Entity.Id,
                    Type = context.CallerEntityProfile.Entity.Type,
                },
                CollectionId = "default",
                Filter = itemFilter
            };

            PlayFabResult<GetInventoryItemsResponse> equippedItems =
                await economyApi.GetInventoryItemsAsync(getEquippedItemsRequest);

            Dictionary<string, PvPItemIdAndTier> itemIdAndTier = new Dictionary<string, PvPItemIdAndTier>();
            foreach (InventoryItem item in equippedItems.Result.Items)
            {
                Tier itemData = JsonConvert.DeserializeObject<Tier>(item.DisplayProperties.ToString());
                itemIdAndTier.Add(item.StackId, new PvPItemIdAndTier() { itemId = item.Id, tier = itemData });
            }

            return itemIdAndTier;
        }

        private static void AssignRandomPlayerTurn(
            PlayerSharedGroupData mySharedGroupInput,
            PlayerSharedGroupData enemySharedGroupInput)
        {
            int randomNumber = new Random().Next(0, 2);

            if (randomNumber == 0)
            {
                mySharedGroupInput.myTurn = true;
                enemySharedGroupInput.myTurn = false;
                int randomCharacter = new Random().Next(0, mySharedGroupInput.selectedCharacters.Count);
                mySharedGroupInput.entityCharacterTurnStackId =
                    mySharedGroupInput.selectedCharacters[randomCharacter].stackId;
            }
            else
            {
                enemySharedGroupInput.myTurn = true;
                mySharedGroupInput.myTurn = false;
                int randomCharacter = new Random().Next(0, enemySharedGroupInput.selectedCharacters.Count);
                enemySharedGroupInput.entityCharacterTurnStackId =
                    enemySharedGroupInput.selectedCharacters[randomCharacter].stackId;
            }

            mySharedGroupInput.isReady = true;
            enemySharedGroupInput.isReady = true;
        }

        private static async Task<dynamic> GetSharedGroupDataAsync(
            PlayFabServerInstanceAPI serverApi,
            string matchId,
            string entityId)
        {
            var getSharedGroupDataRequest = new GetSharedGroupDataRequest
            {
                SharedGroupId = matchId,
            };

            PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);

            return JsonConvert.SerializeObject(getSharedGroupDataResult.Result.Data);
        }

        private static string CheckWinCondition(Dictionary<string, SharedGroupDataRecord> sharedData, string entityId)
        {
            try
            {
                PlayerSharedGroupData playerData = JsonConvert.DeserializeObject<PlayerSharedGroupData>(sharedData[entityId].Value);
                PlayerSharedGroupData player1Data = JsonConvert.DeserializeObject<PlayerSharedGroupData>(sharedData[playerData.myEntityId].Value);
                PlayerSharedGroupData player2Data = JsonConvert.DeserializeObject<PlayerSharedGroupData>(sharedData[playerData.enemyEntityId].Value);

                // Count alive characters for each player
                int player1AliveCount = player1Data.selectedCharacters?.Count ?? 0;
                int player2AliveCount = player2Data.selectedCharacters?.Count ?? 0;

                // Check win conditions
                if (player1AliveCount == 0 && player2AliveCount == 0)
                {
                    // Draw - both players have no alive characters
                    return null;
                }
                else if (player1AliveCount == 0)
                {
                    // Player 2 wins
                    return player2Data.myEntityId;
                }
                else if (player2AliveCount == 0)
                {
                    // Player 1 wins
                    return player1Data.myEntityId;
                }

                // No winner yet
                return null;
            }
            catch
            {
                // If there's any error parsing data, assume no winner
                return null;
            }
        }

        private static async Task<dynamic> UpdateTurnDataAsync(
            PlayFabServerInstanceAPI serverApi,
            FunctionExecutionContext<dynamic> context,
            string matchId,
            string entityId)
        {
            UpdateTurnDataInput turnData = JsonConvert.DeserializeObject<UpdateTurnDataInput>(
                context.FunctionArgument.turnData.ToString()
            );

            var getSharedGroupDataRequest = new GetSharedGroupDataRequest
            {
                SharedGroupId = matchId,
            };

            PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);

            PlayerSharedGroupData mySharedGroup =
                JsonConvert.DeserializeObject<PlayerSharedGroupData>(
                    getSharedGroupDataResult.Result.Data[entityId].Value
                );
            string enemyEntityId = mySharedGroup.enemyEntityId;
            PlayerSharedGroupData enemySharedGroup =
                JsonConvert.DeserializeObject<PlayerSharedGroupData>(
                    getSharedGroupDataResult.Result.Data[enemyEntityId].Value
                );

            // If replay was played, assign turn to the other player
            if (turnData.battleReplayData.played)
            {
                ProcessTurnSwitch(mySharedGroup, enemySharedGroup, turnData);
            }

            await serverApi.UpdateSharedGroupDataAsync(
                new UpdateSharedGroupDataRequest
                {
                    SharedGroupId = matchId,
                    Data = new Dictionary<string, string>
                    {
                        { entityId, JsonConvert.SerializeObject(mySharedGroup) },
                        { enemyEntityId, JsonConvert.SerializeObject(enemySharedGroup) },
                        { "BattleReplayData", JsonConvert.SerializeObject(turnData.battleReplayData) }
                    }
                }
            );

            return new { };
        }

        private static void ProcessTurnSwitch(
            PlayerSharedGroupData mySharedGroup,
            PlayerSharedGroupData enemySharedGroup,
            UpdateTurnDataInput turnData)
        {
            // Update character data from the battle results
            mySharedGroup.selectedCharacters = turnData.playerData.selectedCharacters;
            enemySharedGroup.selectedCharacters = turnData.enemyData.selectedCharacters;

            const float TURN_THRESHOLD = 100f;

            // Initialize turn counters if needed
            InitializeTurnCounters(mySharedGroup, enemySharedGroup);

            // Get all alive characters from both players
            var allCharacters = new List<PvPCharacterInfo>();
            allCharacters.AddRange(mySharedGroup.selectedCharacters);
            allCharacters.AddRange(enemySharedGroup.selectedCharacters);

            if (allCharacters.Count == 0) return;

            // Calculate how many cycles each entity needs to reach the threshold
            float minCyclesNeeded = float.MaxValue;

            foreach (var character in allCharacters)
            {
                float speedIncrement = character.baseSpeed / 100f;
                if (speedIncrement > 0)
                {
                    float currentTurnCounter = GetTurnCounter(mySharedGroup, enemySharedGroup, character.stackId);
                    float cyclesNeeded = (TURN_THRESHOLD - currentTurnCounter) / speedIncrement;
                    minCyclesNeeded = Math.Min(minCyclesNeeded, cyclesNeeded);
                }
            }

            // Apply the minimum cycles needed to all entities and find ready entities
            var readyCharacters = new List<(PvPCharacterInfo character, float turnCounter)>();

            foreach (var character in allCharacters)
            {
                float speedIncrement = character.baseSpeed / 100f;
                float currentCounter = GetTurnCounter(mySharedGroup, enemySharedGroup, character.stackId);
                float newCounter = currentCounter + (speedIncrement * minCyclesNeeded);

                // Update the turn counter
                UpdateTurnCounter(mySharedGroup, enemySharedGroup, character.stackId, newCounter);

                if (newCounter >= TURN_THRESHOLD)
                {
                    readyCharacters.Add((character, newCounter));
                }
            }

            // Sort ready entities based on their turn counter values and entity ID for tiebreaker
            readyCharacters.Sort((a, b) =>
            {
                int turnComparison = b.turnCounter.CompareTo(a.turnCounter);
                if (turnComparison != 0) return turnComparison;

                // Use entity ID as tiebreaker - determine which player owns each character
                string aOwnerEntityId = mySharedGroup.selectedCharacters.Any(c => c.stackId == a.character.stackId)
                    ? mySharedGroup.myEntityId
                    : enemySharedGroup.myEntityId;
                string bOwnerEntityId = mySharedGroup.selectedCharacters.Any(c => c.stackId == b.character.stackId)
                    ? mySharedGroup.myEntityId
                    : enemySharedGroup.myEntityId;

                return string.Compare(bOwnerEntityId, aOwnerEntityId, StringComparison.Ordinal);
            });

            // Get the character with the next turn
            if (readyCharacters.Count > 0)
            {
                var nextTurnCharacter = readyCharacters[0].character;

                // Subtract threshold from the selected character's turn counter
                float currentCounter = GetTurnCounter(mySharedGroup, enemySharedGroup, nextTurnCharacter.stackId);
                UpdateTurnCounter(mySharedGroup, enemySharedGroup, nextTurnCharacter.stackId, currentCounter - TURN_THRESHOLD);

                // Determine which player owns this character and assign turn
                bool isMyCharacterTurn = mySharedGroup.selectedCharacters
                    .Any(c => c.stackId == nextTurnCharacter.stackId);

                if (isMyCharacterTurn)
                {
                    mySharedGroup.myTurn = true;
                    enemySharedGroup.myTurn = false;
                    mySharedGroup.entityCharacterTurnStackId = nextTurnCharacter.stackId;
                }
                else
                {
                    enemySharedGroup.myTurn = true;
                    mySharedGroup.myTurn = false;
                    enemySharedGroup.entityCharacterTurnStackId = nextTurnCharacter.stackId;
                }
            }
        }

        private static void InitializeTurnCounters(PlayerSharedGroupData mySharedGroup, PlayerSharedGroupData enemySharedGroup)
        {
            if (mySharedGroup.turnCounters == null)
                mySharedGroup.turnCounters = new Dictionary<string, float>();
            if (enemySharedGroup.turnCounters == null)
                enemySharedGroup.turnCounters = new Dictionary<string, float>();

            // Initialize counters for new characters
            foreach (var character in mySharedGroup.selectedCharacters)
            {
                if (!mySharedGroup.turnCounters.ContainsKey(character.stackId))
                    mySharedGroup.turnCounters[character.stackId] = 0f;
            }

            foreach (var character in enemySharedGroup.selectedCharacters)
            {
                if (!enemySharedGroup.turnCounters.ContainsKey(character.stackId))
                    enemySharedGroup.turnCounters[character.stackId] = 0f;
            }
        }

        private static float GetTurnCounter(PlayerSharedGroupData mySharedGroup, PlayerSharedGroupData enemySharedGroup, string stackId)
        {
            if (mySharedGroup.turnCounters?.ContainsKey(stackId) == true)
                return mySharedGroup.turnCounters[stackId];
            if (enemySharedGroup.turnCounters?.ContainsKey(stackId) == true)
                return enemySharedGroup.turnCounters[stackId];
            return 0f;
        }

        private static void UpdateTurnCounter(PlayerSharedGroupData mySharedGroup, PlayerSharedGroupData enemySharedGroup, string stackId, float value)
        {
            if (mySharedGroup.turnCounters?.ContainsKey(stackId) == true)
                mySharedGroup.turnCounters[stackId] = value;
            else if (enemySharedGroup.turnCounters?.ContainsKey(stackId) == true)
                enemySharedGroup.turnCounters[stackId] = value;
        }

        private static async Task<dynamic> GetTurnDataAsync(
            PlayFabServerInstanceAPI serverApi,
            string matchId,
            string entityId)
        {
            var getSharedGroupDataRequest = new GetSharedGroupDataRequest
            {
                SharedGroupId = matchId,
            };

            PlayFabResult<GetSharedGroupDataResult> getSharedGroupDataResult =
                await serverApi.GetSharedGroupDataAsync(getSharedGroupDataRequest);

            return JsonConvert.SerializeObject(getSharedGroupDataResult.Result.Data);
        }

        [FunctionName("PvP")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log
        )
        {
            var context = await PlayFabUtil.ParseFunctionContext(req);

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
            var multiplayerApi = new PlayFabMultiplayerInstanceAPI(apiSettings, titleContext);

            string entityId = context.CallerEntityProfile.Entity.Id;
            string actionType = context.FunctionArgument.actionType;
            string matchId = context.FunctionArgument.matchId;

            switch (actionType)
            {
                case "CreateSharedGroup":
                    return await CreateSharedGroupAsync(serverApi, multiplayerApi, matchId, entityId);

                case "SelectCharacter":
                    return await SelectCharacterAsync(serverApi, economyApi, context, matchId, entityId);

                case "GetSharedGroupData":
                    return await GetSharedGroupDataAsync(serverApi, matchId, entityId);

                case "UpdateTurnData":
                    return await UpdateTurnDataAsync(serverApi, context, matchId, entityId);

                case "GetTurnData":
                    return await GetTurnDataAsync(serverApi, matchId, entityId);
            }

            return new { };
        }
    }
}
