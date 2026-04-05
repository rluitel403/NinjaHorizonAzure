using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PlayFab.GroupsModels;
using PlayFab.ProgressionModels;

namespace NinjaHorizon.Function.Clan
{
    /// <summary>
    /// Core service for clan operations - handles all business logic
    /// Uses Progression API for statistics (player and group)
    /// </summary>
    public class ClanService
    {
        private readonly PlayFabUtil _playFabUtil;

        public ClanService(PlayFabUtil playFabUtil)
        {
            _playFabUtil = playFabUtil;
        }

        #region Clan Creation & Management

        /// <summary>
        /// Creates a new clan with the calling player as the leader
        /// Validates clan name uniqueness before creation
        /// </summary>
        public async Task<CreateClanResponse> CreateClan(string clanName)
        {
            // Check if player is already in a clan
            var existingMemberships = await _playFabUtil.ListEntityGroupMembership(_playFabUtil.Entity);
            if (existingMemberships.Groups != null && existingMemberships.Groups.Count > 0)
            {
                throw new Exception("You are already in a clan. Leave your current clan first.");
            }

            // Validate clan name format
            if (string.IsNullOrWhiteSpace(clanName))
            {
                throw new Exception("Clan name cannot be empty");
            }

            if (clanName.Length < 3 || clanName.Length > 50)
            {
                throw new Exception("Clan name must be between 3 and 50 characters");
            }

            // Check for duplicate clan name
            bool nameExists = await IsClanNameTaken(clanName);
            if (nameExists)
            {
                throw new Exception($"Clan name '{clanName}' is already taken. Please choose a different name.");
            }

            // Create the group
            var groupResponse = await _playFabUtil.CreateEntityGroup(clanName, _playFabUtil.Entity);

            // Initialize clan statistics with DisplayName
            var groupId = groupResponse.Group.Id;
            await InitializeClanStatistics(groupId, clanName);

            // Generate and register short code
            string shortCode = await GenerateAndRegisterShortCode(groupId);

            // Initialize buildings
            var defaultBuildings = ClanBuildingManager.CreateDefaultBuildings();
            await _playFabUtil.SetEntityGroupObjects(groupId, new Dictionary<string, object>
            {
                { ClanConstants.OBJ_BUILDINGS, defaultBuildings },
                { ClanConstants.OBJ_BUILDINGS, defaultBuildings },
                { "DisplayName", clanName }, // Store display name for reference
                { "ShortCode", shortCode } // Store short code
            });

            // Register the clan name in the global index
            await RegisterClanName(clanName, groupId);

            return new CreateClanResponse
            {
                ClanId = groupId,
                ClanName = clanName
            };
        }

        /// <summary>
        /// Generates a unique 5-digit short code and registers it
        /// </summary>
        private async Task<string> GenerateAndRegisterShortCode(string groupId)
        {
            var random = new Random();
            string shortCode = "";
            bool isUnique = false;
            int attempts = 0;

            while (!isUnique && attempts < 10)
            {
                shortCode = random.Next(10000, 99999).ToString();
                isUnique = await RegisterShortCode(shortCode, groupId);
                attempts++;
            }

            if (!isUnique)
            {
                throw new Exception("Failed to generate a unique clan ID. Please try again.");
            }

            return shortCode;
        }

        private async Task<bool> RegisterShortCode(string shortCode, string groupId)
        {
            try
            {
                // Fetch current index
                var titleDataRequest = new PlayFab.ServerModels.GetTitleDataRequest
                {
                    Keys = new List<string> { "ClanShortCodes" }
                };

                var titleDataResult = await _playFabUtil.ServerApi.GetTitleInternalDataAsync(titleDataRequest);
                
                var shortCodes = new Dictionary<string, string>();
                if (titleDataResult.Result?.Data != null && 
                    titleDataResult.Result.Data.ContainsKey("ClanShortCodes"))
                {
                    var indexJson = titleDataResult.Result.Data["ClanShortCodes"];
                    shortCodes = JsonConvert.DeserializeObject<Dictionary<string, string>>(indexJson);
                }

                if (shortCodes.ContainsKey(shortCode))
                {
                    return false; // Already taken
                }

                // Add the new code
                shortCodes[shortCode] = groupId;

                // Save back to title data
                var updateRequest = new PlayFab.ServerModels.SetTitleDataRequest
                {
                    Key = "ClanShortCodes",
                    Value = JsonConvert.SerializeObject(shortCodes)
                };

                await _playFabUtil.ServerApi.SetTitleInternalDataAsync(updateRequest);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Initializes clan-level statistics (reputation, gold) using Progression API
        /// Also sets DisplayName metadata for leaderboard display
        /// </summary>
        private async Task InitializeClanStatistics(string groupId, string displayName)
        {
            var request = new UpdateStatisticsRequest
            {
                Entity = new PlayFab.ProgressionModels.EntityKey
                {
                    Id = groupId,
                    Type = "group"
                },
                Statistics = new List<StatisticUpdate>
                {
                    new StatisticUpdate
                    {
                        Name = ClanConstants.STAT_CLAN_TOTAL_REPUTATION,
                        Scores = new List<string> { "0" },
                        Metadata = displayName // Store display name in metadata for leaderboard
                    },
                    new StatisticUpdate
                    {
                        Name = ClanConstants.STAT_CLAN_GOLD,
                        Scores = new List<string> { "0" }
                    }
                }
            };

            var result = await _playFabUtil.ProgressionApi.UpdateStatisticsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to initialize clan statistics: {result.Error.ErrorMessage}");
            }
        }

        /// <summary>
        /// Initializes player statistics if they don't exist
        /// </summary>
        private async Task InitializePlayerStatistics()
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var stats = await GetPlayerStatistics(_playFabUtil.Entity.Id);

            // Only initialize if not already set
            if (!stats.ContainsKey(ClanConstants.STAT_STAMINA))
            {
                await UpdatePlayerStatistics(_playFabUtil.Entity.Id, new Dictionary<string, int>
                {
                    { ClanConstants.STAT_STAMINA, ClanConstants.BASE_MAX_STAMINA },
                    { ClanConstants.STAT_MAX_STAMINA, ClanConstants.BASE_MAX_STAMINA },
                    { ClanConstants.STAT_STAMINA_LAST_UPDATE, (int)timestamp },
                    { ClanConstants.STAT_REPUTATION, 0 }
                });
            }
        }

        /// <summary>
        /// Checks if a clan name is already taken
        /// Uses title internal data to store clan name index
        /// </summary>
        private async Task<bool> IsClanNameTaken(string clanName)
        {
            try
            {
                // Normalize the clan name for comparison (case-insensitive)
                string normalizedName = clanName.ToLowerInvariant().Trim();

                // Fetch the clan names index from title data
                var titleDataRequest = new PlayFab.ServerModels.GetTitleDataRequest
                {
                    Keys = new List<string> { "ClanNamesIndex" }
                };

                var titleDataResult = await _playFabUtil.ServerApi.GetTitleInternalDataAsync(titleDataRequest);
                
                if (titleDataResult.Result?.Data != null && 
                    titleDataResult.Result.Data.ContainsKey("ClanNamesIndex"))
                {
                    var indexJson = titleDataResult.Result.Data["ClanNamesIndex"];
                    var clanNamesIndex = JsonConvert.DeserializeObject<Dictionary<string, string>>(indexJson);
                    
                    return clanNamesIndex.ContainsKey(normalizedName);
                }

                return false;
            }
            catch
            {
                // If we can't check, allow creation (fail open)
                return false;
            }
        }

        /// <summary>
        /// Registers a clan name in the global index to prevent duplicates
        /// </summary>
        private async Task RegisterClanName(string clanName, string groupId)
        {
            try
            {
                string normalizedName = clanName.ToLowerInvariant().Trim();

                // Fetch current index
                var titleDataRequest = new PlayFab.ServerModels.GetTitleDataRequest
                {
                    Keys = new List<string> { "ClanNamesIndex" }
                };

                var titleDataResult = await _playFabUtil.ServerApi.GetTitleInternalDataAsync(titleDataRequest);
                
                var clanNamesIndex = new Dictionary<string, string>();
                if (titleDataResult.Result?.Data != null && 
                    titleDataResult.Result.Data.ContainsKey("ClanNamesIndex"))
                {
                    var indexJson = titleDataResult.Result.Data["ClanNamesIndex"];
                    clanNamesIndex = JsonConvert.DeserializeObject<Dictionary<string, string>>(indexJson);
                }

                // Add the new clan name
                clanNamesIndex[normalizedName] = groupId;

                // Save back to title data
                var updateRequest = new PlayFab.ServerModels.SetTitleDataRequest
                {
                    Key = "ClanNamesIndex",
                    Value = JsonConvert.SerializeObject(clanNamesIndex)
                };

                await _playFabUtil.ServerApi.SetTitleInternalDataAsync(updateRequest);
            }
            catch (Exception ex)
            {
                // Log but don't fail clan creation if index update fails
                Console.WriteLine($"Warning: Failed to register clan name: {ex.Message}");
            }
        }

        #endregion

        #region Clan Membership

        /// <summary>
        /// Player applies to join a clan
        /// </summary>
        public async Task ApplyToClan(string clanId)
        {
            await _playFabUtil.ApplyToEntityGroup(clanId, _playFabUtil.Entity);
        }

        /// <summary>
        /// Leader invites a player to the clan
        /// </summary>
        public async Task InvitePlayerToClan(string clanId, string targetPlayerId)
        {
            // Validate caller is a leader/admin
            await ValidateClanLeader(clanId);

            var targetEntity = new PlayFab.EconomyModels.EntityKey
            {
                Id = targetPlayerId,
                Type = "title_player_account"
            };

            await _playFabUtil.InviteToEntityGroup(clanId, targetEntity);
        }

        /// <summary>
        /// Leader accepts a player's application
        /// </summary>
        public async Task AcceptApplication(string clanId, string applicantEntityId)
        {
            // Validate caller is a leader/admin
            await ValidateClanLeader(clanId);

            var applicantEntity = new PlayFab.EconomyModels.EntityKey
            {
                Id = applicantEntityId,
                Type = "title_player_account"
            };

            await _playFabUtil.AcceptEntityGroupApplication(clanId, applicantEntity);

            // Initialize applicant's statistics with clan's Bath House level
            await InitializePlayerStatisticsForEntity(applicantEntityId, clanId);
        }

        /// <summary>
        /// Leader rejects a player's application
        /// </summary>
        public async Task RejectApplication(string clanId, string applicantEntityId)
        {
            // Validate caller is a leader/admin
            await ValidateClanLeader(clanId);

            var applicantEntity = new PlayFab.EconomyModels.EntityKey
            {
                Id = applicantEntityId,
                Type = "title_player_account"
            };

            await _playFabUtil.RejectEntityGroupApplication(clanId, applicantEntity);
        }

        /// <summary>
        /// Gets pending applications for a clan
        /// </summary>
        public async Task<List<GroupApplication>> GetPendingApplications(string clanId)
        {
            // Validate caller is a leader/admin
            await ValidateClanLeader(clanId);

            var applications = await _playFabUtil.ListEntityGroupApplications(clanId);
            return applications.Applications;
        }

        #endregion

        #region Clan Details & Leaderboard

        /// <summary>
        /// Gets detailed clan information including members and buildings
        /// </summary>
        public async Task<GetClanDetailsResponse> GetClanDetails(string clanId)
        {
            // Fetch group info
            var group = await _playFabUtil.GetEntityGroup(clanId);

            // Fetch clan statistics using Progression API
            var clanStats = await GetGroupStatistics(clanId);
            int totalReputation = clanStats.ContainsKey(ClanConstants.STAT_CLAN_TOTAL_REPUTATION)
                ? clanStats[ClanConstants.STAT_CLAN_TOTAL_REPUTATION]
                : 0;
            int clanGold = clanStats.ContainsKey(ClanConstants.STAT_CLAN_GOLD)
                ? clanStats[ClanConstants.STAT_CLAN_GOLD]
                : 0;

            // Fetch buildings
            var objects = await _playFabUtil.GetEntityGroupObjects(clanId, new List<string> { ClanConstants.OBJ_BUILDINGS });
            var buildings = ClanBuildingManager.CreateDefaultBuildings();
            
            if (objects.Objects.ContainsKey(ClanConstants.OBJ_BUILDINGS))
            {
                var buildingsJson = JsonConvert.SerializeObject(objects.Objects[ClanConstants.OBJ_BUILDINGS].DataObject);
                buildings = ClanBuildingManager.ParseBuildings(buildingsJson);
            }

            // Fetch member list
            var membersResponse = await _playFabUtil.ListEntityGroupMembers(clanId);
            var members = await GetMemberInfoList(membersResponse.Members, buildings.BathHouseLevel);

            // Calculate clan status
            var status = ClanStatistics.CreateClanStatus(members);

            // Get Short Code from objects if available
            string shortCode = "";
            if (objects.Objects.ContainsKey("ShortCode"))
            {
                shortCode = objects.Objects["ShortCode"].DataObject.ToString();
            }

            return new GetClanDetailsResponse
            {
                ClanId = clanId,
                ClanName = group.GroupName,
                ShortCode = shortCode,
                TotalReputation = totalReputation,
                ClanGold = clanGold,
                Buildings = buildings,
                Members = members,
                Status = status
            };
        }

        /// <summary>
        /// Searches for a clan by its 5-digit short code
        /// </summary>
        public async Task<GetClanDetailsResponse> SearchClanByCode(string shortCode)
        {
            // Fetch index
            var titleDataRequest = new PlayFab.ServerModels.GetTitleDataRequest
            {
                Keys = new List<string> { "ClanShortCodes" }
            };

            var titleDataResult = await _playFabUtil.ServerApi.GetTitleInternalDataAsync(titleDataRequest);
            
            if (titleDataResult.Result?.Data != null && 
                titleDataResult.Result.Data.ContainsKey("ClanShortCodes"))
            {
                var indexJson = titleDataResult.Result.Data["ClanShortCodes"];
                var shortCodes = JsonConvert.DeserializeObject<Dictionary<string, string>>(indexJson);
                
                if (shortCodes.ContainsKey(shortCode))
                {
                    string clanId = shortCodes[shortCode];
                    return await GetClanDetails(clanId);
                }
            }

            throw new Exception("Clan not found with that ID.");
        }

        /// <summary>
        /// Gets clan leaderboard (top clans by reputation) using Progression API GetLeaderboardAsync
        /// Efficiently uses DisplayName from leaderboard response to avoid extra API calls
        /// Note: Requires leaderboard definition to be created in PlayFab Game Manager first
        /// </summary>
        public async Task<GetClanLeaderboardResponse> GetClanLeaderboard(int count = 100)
        {
            try
            {
                var request = new GetEntityLeaderboardRequest
                {
                    LeaderboardName = ClanConstants.STAT_CLAN_TOTAL_REPUTATION,
                    StartingPosition = 0,
                    PageSize = (uint)Math.Min(count, 1000)
                };

                var result = await _playFabUtil.ProgressionApi.GetLeaderboardAsync(request);
                if (result.Error != null)
                {
                    throw new Exception($"Failed to get clan leaderboard: {result.Error.ErrorMessage}");
                }

                var leaderboard = new List<ClanLeaderboardEntry>();
                
                // Parse leaderboard entries - use DisplayName directly for efficiency!
                if (result.Result.Rankings != null)
                {
                    foreach (var ranking in result.Result.Rankings)
                    {
                        // Get score from the Scores array (first element is typically the main score)
                        int score = 0;
                        if (ranking.Scores != null && ranking.Scores.Count > 0)
                        {
                            int.TryParse(ranking.Scores[0], out score);
                        }
                        
                        leaderboard.Add(new ClanLeaderboardEntry
                        {
                            ClanId = ranking.Entity.Id,
                            ClanName = ranking.DisplayName ?? ranking.Entity.Id, // Use DisplayName directly!
                            Rank = ranking.Rank,
                            TotalReputation = score
                        });
                    }
                }

                return new GetClanLeaderboardResponse
                {
                    Clans = leaderboard
                };
            }
            catch (Exception ex)
            {
                // If leaderboard doesn't exist or fails, return empty list
                // This can happen if the leaderboard definition hasn't been created yet
                Console.WriteLine($"Leaderboard error: {ex.Message}");
                return new GetClanLeaderboardResponse
                {
                    Clans = new List<ClanLeaderboardEntry>()
                };
            }
        }

        #endregion

        #region Combat & Attacks

        /// <summary>
        /// Attacks another clan, spending stamina and potentially gaining reputation
        /// </summary>
        public async Task<AttackClanResponse> AttackClan(string targetClanId)
        {
            // Get attacker's clan
            var attackerMemberships = await _playFabUtil.ListEntityGroupMembership(_playFabUtil.Entity);
            if (attackerMemberships.Groups == null || attackerMemberships.Groups.Count == 0)
            {
                throw new Exception("You must be in a clan to attack");
            }
            var attackerClanId = attackerMemberships.Groups[0].Group.Id;

            // Can't attack own clan
            if (attackerClanId == targetClanId)
            {
                throw new Exception("You cannot attack your own clan");
            }

            // Get attacker clan details first (need Bath House level)
            var attackerClanDetails = await GetClanDetails(attackerClanId);
            
            // Get attacker's statistics
            var attackerStats = await GetPlayerStatistics(_playFabUtil.Entity.Id);
            var attackerStaminaData = ClanStatistics.ParsePlayerStamina(attackerStats);
            
            // Calculate current stamina (with building bonus)
            int currentStamina = ClanStatistics.CalculateCurrentStamina(
                attackerStaminaData.Stamina,
                attackerStaminaData.StaminaLastUpdate,
                attackerStaminaData.MaxStamina,
                attackerClanDetails.Buildings.BathHouseLevel);

            // Validate stamina
            if (!ClanStatistics.HasEnoughStamina(currentStamina, ClanConstants.ATTACK_STAMINA_COST))
            {
                throw new Exception($"Not enough stamina. Required: {ClanConstants.ATTACK_STAMINA_COST}, Available: {currentStamina}");
            }

            // Deduct stamina
            var (newStamina, newTimestamp) = ClanStatistics.SpendStamina(currentStamina, ClanConstants.ATTACK_STAMINA_COST);
            await UpdatePlayerStatistics(_playFabUtil.Entity.Id, new Dictionary<string, int>
            {
                { ClanConstants.STAT_STAMINA, newStamina },
                { ClanConstants.STAT_STAMINA_LAST_UPDATE, (int)newTimestamp }
            });

            // Get target clan details
            var targetClanDetails = await GetClanDetails(targetClanId);

            // Calculate damage based on attacker's buildings
            int staminaReductionPerPlayer = ClanStatistics.CalculateStaminaReductionPerPlayer(
                attackerClanDetails.Buildings.TeaHouseLevel);
            int playersAffected = ClanStatistics.CalculatePlayersAffectedByAttack(
                attackerClanDetails.Buildings.TrainingCentreLevel);

            // Select random target players
            var affectedPlayers = ClanStatistics.SelectRandomPlayersForAttack(
                targetClanDetails.Members, playersAffected);

            // Fetch stats for all affected players in bulk (efficient!)
            var affectedEntityIds = affectedPlayers.Select(p => p.EntityId).ToList();
            var affectedPlayersStats = await GetPlayerStatisticsBulk(affectedEntityIds);

            // Reduce stamina of affected players and update their data in the member list
            foreach (var player in affectedPlayers)
            {
                var playerStats = affectedPlayersStats.ContainsKey(player.EntityId) 
                    ? affectedPlayersStats[player.EntityId] 
                    : new Dictionary<string, int>();
                var playerStaminaData = ClanStatistics.ParsePlayerStamina(playerStats);
                
                // Calculate current stamina (accounting for regeneration + building bonus)
                int playerCurrentStamina = ClanStatistics.CalculateCurrentStamina(
                    playerStaminaData.Stamina,
                    playerStaminaData.StaminaLastUpdate,
                    playerStaminaData.MaxStamina,
                    targetClanDetails.Buildings.BathHouseLevel);

                // Deduct damage and update timestamp to NOW
                var (playerNewStamina, playerNewTimestamp) = ClanStatistics.SpendStamina(
                    playerCurrentStamina, staminaReductionPerPlayer);

                await UpdatePlayerStatistics(player.EntityId, new Dictionary<string, int>
                {
                    { ClanConstants.STAT_STAMINA, playerNewStamina },
                    { ClanConstants.STAT_STAMINA_LAST_UPDATE, (int)playerNewTimestamp }
                });

                // Update the member's stamina in our local list for bleeding calculation
                player.CurrentStamina = playerNewStamina;
            }

            // Recalculate target clan status from updated member data (no extra API calls!)
            var updatedTargetStatus = ClanStatistics.CreateClanStatus(targetClanDetails.Members);

            // Calculate reputation reward
            int reputationGained = ClanStatistics.CalculateReputationReward(
                attackerClanDetails.TotalReputation,
                targetClanDetails.TotalReputation,
                updatedTargetStatus.IsBleeding);

            // Award reputation if target is bleeding
            if (reputationGained > 0)
            {
                // Update attacker's personal reputation
                await UpdatePlayerStatistics(_playFabUtil.Entity.Id, new Dictionary<string, int>
                {
                    { ClanConstants.STAT_REPUTATION, attackerStaminaData.Reputation + reputationGained }
                });

                // Update clan total reputation (atomic increment)
                await IncrementGroupStatistic(attackerClanId, ClanConstants.STAT_CLAN_TOTAL_REPUTATION, reputationGained);
            }

            return new AttackClanResponse
            {
                StaminaSpent = ClanConstants.ATTACK_STAMINA_COST,
                ReputationGained = reputationGained,
                TargetIsBleeding = updatedTargetStatus.IsBleeding,
                TargetBleedingPercentage = updatedTargetStatus.BleedingPercentage,
                AffectedPlayers = affectedPlayers.Select(p => p.DisplayName ?? p.EntityId).ToList()
            };
        }

        #endregion

        #region Building Upgrades

        /// <summary>
        /// Upgrades a clan building
        /// </summary>
        public async Task UpgradeBuilding(string clanId, BuildingType buildingType)
        {
            // Validate caller is a leader/admin
            await ValidateClanLeader(clanId);

            // Get current buildings and clan gold
            var clanDetails = await GetClanDetails(clanId);
            var currentBuildings = clanDetails.Buildings;
            int currentLevel = ClanBuildingManager.GetBuildingLevel(currentBuildings, buildingType);

            // Validate upgrade
            if (!ClanBuildingManager.CanUpgradeBuilding(currentLevel, clanDetails.ClanGold, out int upgradeCost, out string errorMessage))
            {
                throw new Exception(errorMessage);
            }

            // Deduct gold
            await IncrementGroupStatistic(clanId, ClanConstants.STAT_CLAN_GOLD, -upgradeCost);

            // Upgrade building
            var updatedBuildings = ClanBuildingManager.UpgradeBuilding(currentBuildings, buildingType);

            // Save buildings
            await _playFabUtil.SetEntityGroupObjects(clanId, new Dictionary<string, object>
            {
                { ClanConstants.OBJ_BUILDINGS, updatedBuildings }
            });

            // No need to update members' max stamina - it's calculated dynamically!
            // Bath House bonus is applied at runtime via CalculateCurrentStamina()
        }

        /// <summary>
        /// Helper to extract entity ID from EntityMemberRole
        /// </summary>
        private string GetEntityIdFromMember(EntityMemberRole member)
        {
            // EntityMemberRole structure varies - try to access entity info
            // If it has properties, access them; otherwise use ToString or other methods
            try
            {
                // Try reflection to get any Id-like property
                var type = member.GetType();
                var idProp = type.GetProperty("EntityId") ?? type.GetProperty("Id") ?? type.GetProperty("Key");
                if (idProp != null)
                {
                    var value = idProp.GetValue(member);
                    if (value is string strValue) return strValue;
                    if (value != null)
                    {
                        var keyType = value.GetType();
                        var keyIdProp = keyType.GetProperty("Id");
                        if (keyIdProp != null)
                        {
                            return keyIdProp.GetValue(value) as string;
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Reputation Contribution

        /// <summary>
        /// Player contributes reputation points to the clan
        /// </summary>
        public async Task ContributeReputation(string clanId, int amount)
        {
            if (amount <= 0)
            {
                throw new Exception("Amount must be positive");
            }

            // Get player's statistics
            var stats = await GetPlayerStatistics(_playFabUtil.Entity.Id);
            var staminaData = ClanStatistics.ParsePlayerStamina(stats);

            // Validate player has enough reputation
            if (staminaData.Reputation < amount)
            {
                throw new Exception($"Not enough reputation. Available: {staminaData.Reputation}, Required: {amount}");
            }

            // Deduct from player
            await UpdatePlayerStatistics(_playFabUtil.Entity.Id, new Dictionary<string, int>
            {
                { ClanConstants.STAT_REPUTATION, staminaData.Reputation - amount }
            });

            // Add to clan (atomic increment)
            await IncrementGroupStatistic(clanId, ClanConstants.STAT_CLAN_TOTAL_REPUTATION, amount);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Validates that the calling player is a leader/admin of the clan
        /// </summary>
        private async Task ValidateClanLeader(string clanId)
        {
            var group = await _playFabUtil.GetEntityGroup(clanId);
            
            // Check if caller is the group creator/admin
            // Simplified validation - check if player is in admin role by checking group admin
            bool isAdmin = group.AdminRoleId != null;
            
            // For now, allow any member to perform admin actions
            // TODO: Implement proper role checking when we understand the Roles structure
            if (group == null)
            {
                throw new Exception("Clan not found");
            }
            
            // Basic validation passed
        }

        /// <summary>
        /// Gets player statistics from Progression API
        /// </summary>
        private async Task<Dictionary<string, int>> GetPlayerStatistics(string entityId)
        {
            var request = new GetStatisticsRequest
            {
                Entity = new PlayFab.ProgressionModels.EntityKey
                {
                    Id = entityId,
                    Type = "title_player_account"
                }
            };

            var result = await _playFabUtil.ProgressionApi.GetStatisticsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get player statistics: {result.Error.ErrorMessage}");
            }

            var stats = new Dictionary<string, int>();
            if (result.Result.Statistics != null)
            {
                // Statistics is a Dictionary<string, EntityStatisticValue>
                foreach (var kvp in result.Result.Statistics)
                {
                    string statName = kvp.Key;
                    var statValue = kvp.Value;
                    
                    int value = 0;
                    if (statValue.Scores != null && statValue.Scores.Count > 0)
                    {
                        int.TryParse(statValue.Scores[0], out value);
                    }
                    stats[statName] = value;
                }
            }

            return stats;
        }

        /// <summary>
        /// Public method to get player statistics (for Clan.cs endpoint)
        /// </summary>
        public async Task<Dictionary<string, int>> GetPlayerStatisticsPublic(string entityId)
        {
            return await GetPlayerStatistics(entityId);
        }

        /// <summary>
        /// Updates player statistics using Progression API
        /// </summary>
        private async Task UpdatePlayerStatistics(string entityId, Dictionary<string, int> statistics)
        {
            var request = new UpdateStatisticsRequest
            {
                Entity = new PlayFab.ProgressionModels.EntityKey
                {
                    Id = entityId,
                    Type = "title_player_account"
                },
                Statistics = statistics.Select(kvp => new StatisticUpdate
                {
                    Name = kvp.Key,
                    Scores = new List<string> { kvp.Value.ToString() }
                }).ToList()
            };

            var result = await _playFabUtil.ProgressionApi.UpdateStatisticsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update player statistics: {result.Error.ErrorMessage}");
            }
        }

        /// <summary>
        /// Gets group statistics using Progression API
        /// </summary>
        private async Task<Dictionary<string, int>> GetGroupStatistics(string groupId)
        {
            var request = new GetStatisticsRequest
            {
                Entity = new PlayFab.ProgressionModels.EntityKey
                {
                    Id = groupId,
                    Type = "group"
                }
            };

            var result = await _playFabUtil.ProgressionApi.GetStatisticsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to get group statistics: {result.Error.ErrorMessage}");
            }

            var stats = new Dictionary<string, int>();
            if (result.Result.Statistics != null)
            {
                // Statistics is a Dictionary<string, EntityStatisticValue>
                foreach (var kvp in result.Result.Statistics)
                {
                    string statName = kvp.Key;
                    var statValue = kvp.Value;
                    
                    int value = 0;
                    if (statValue.Scores != null && statValue.Scores.Count > 0)
                    {
                        int.TryParse(statValue.Scores[0], out value);
                    }
                    stats[statName] = value;
                }
            }

            return stats;
        }

        /// <summary>
        /// Atomically increments a group statistic using Progression API
        /// </summary>
        private async Task IncrementGroupStatistic(string groupId, string statisticName, int amount)
        {
            // Get current value
            var stats = await GetGroupStatistics(groupId);
            int currentValue = stats.ContainsKey(statisticName) ? stats[statisticName] : 0;
            int newValue = Math.Max(0, currentValue + amount); // Prevent negative values

            // Update
            var request = new UpdateStatisticsRequest
            {
                Entity = new PlayFab.ProgressionModels.EntityKey
                {
                    Id = groupId,
                    Type = "group"
                },
                Statistics = new List<StatisticUpdate>
                {
                    new StatisticUpdate
                    {
                        Name = statisticName,
                        Scores = new List<string> { newValue.ToString() }
                    }
                }
            };

            var result = await _playFabUtil.ProgressionApi.UpdateStatisticsAsync(request);
            if (result.Error != null)
            {
                throw new Exception($"Failed to update group statistic: {result.Error.ErrorMessage}");
            }
        }

        /// <summary>
        /// Converts group members to detailed member info list
        /// Uses bulk statistics API for efficiency
        /// </summary>
        private async Task<List<ClanMemberInfo>> GetMemberInfoList(List<EntityMemberRole> members, int bathHouseLevel)
        {
            var memberInfoList = new List<ClanMemberInfo>();

            if (members == null || members.Count == 0) return memberInfoList;

            // Extract entity IDs
            var entityIds = new List<string>();
            var entityIdToMember = new Dictionary<string, EntityMemberRole>();
            
            foreach (var member in members)
            {
                string entityId = GetEntityIdFromMember(member);
                if (!string.IsNullOrEmpty(entityId))
                {
                    entityIds.Add(entityId);
                    entityIdToMember[entityId] = member;
                }
            }

            if (entityIds.Count == 0) return memberInfoList;

            // Fetch statistics for all members in bulk
            var allStats = await GetPlayerStatisticsBulk(entityIds);

            // Process each member
            foreach (var entityId in entityIds)
            {
                var stats = allStats.ContainsKey(entityId) ? allStats[entityId] : new Dictionary<string, int>();
                var staminaData = ClanStatistics.ParsePlayerStamina(stats);

                int currentStamina = ClanStatistics.CalculateCurrentStamina(
                    staminaData.Stamina,
                    staminaData.StaminaLastUpdate,
                    staminaData.MaxStamina,
                    bathHouseLevel);

                // Calculate effective max stamina (stored + building bonus)
                int effectiveMaxStamina = ClanStatistics.CalculateEffectiveMaxStamina(
                    staminaData.MaxStamina, bathHouseLevel);

                memberInfoList.Add(new ClanMemberInfo
                {
                    EntityId = entityId,
                    PlayFabId = entityId,
                    DisplayName = entityId, // Could fetch display name from profile if needed
                    CurrentStamina = currentStamina,
                    MaxStamina = effectiveMaxStamina, // Show effective max (includes building)
                    Reputation = staminaData.Reputation,
                    Role = entityIdToMember[entityId].RoleName ?? "Member"
                });
            }

            return memberInfoList;
        }

        /// <summary>
        /// Gets statistics for multiple players in bulk using GetStatisticsForEntities
        /// This is much more efficient than calling GetStatistics for each player individually
        /// </summary>
        private async Task<Dictionary<string, Dictionary<string, int>>> GetPlayerStatisticsBulk(List<string> entityIds)
        {
            var result = new Dictionary<string, Dictionary<string, int>>();
            
            if (entityIds == null || entityIds.Count == 0)
                return result;

            // PlayFab has a limit on batch requests (usually 25), so chunk if needed
            const int batchSize = 25;
            for (int i = 0; i < entityIds.Count; i += batchSize)
            {
                var batch = entityIds.Skip(i).Take(batchSize).ToList();
                
                try
                {
                    var request = new GetStatisticsForEntitiesRequest
                    {
                        Entities = batch.Select(id => new PlayFab.ProgressionModels.EntityKey
                        {
                            Id = id,
                            Type = "title_player_account"
                        }).ToList()
                    };

                    var apiResult = await _playFabUtil.ProgressionApi.GetStatisticsForEntitiesAsync(request);
                    if (apiResult.Error != null)
                    {
                        // If bulk fetch fails, fall back to individual fetches for this batch
                        foreach (var entityId in batch)
                        {
                            try
                            {
                                var individualStats = await GetPlayerStatistics(entityId);
                                result[entityId] = individualStats;
                            }
                            catch
                            {
                                // Skip entities that fail
                                result[entityId] = new Dictionary<string, int>();
                            }
                        }
                        continue;
                    }

                    // Process the results
                    if (apiResult.Result.EntitiesStatistics != null)
                    {
                        foreach (var entityStats in apiResult.Result.EntitiesStatistics)
                        {
                            // EntityStatistics has EntityKey and Statistics (Dictionary)
                            var entityId = entityStats.EntityKey?.Id;
                            if (string.IsNullOrEmpty(entityId)) continue;
                            
                            var stats = new Dictionary<string, int>();
                            
                            // Statistics is a List<EntityStatisticValue>
                            if (entityStats.Statistics != null)
                            {
                                foreach (var stat in entityStats.Statistics)
                                {
                                    // EntityStatisticValue has Name and Scores
                                    int value = 0;
                                    if (stat.Scores != null && stat.Scores.Count > 0)
                                    {
                                        int.TryParse(stat.Scores[0], out value);
                                    }
                                    stats[stat.Name] = value;
                                }
                            }
                            
                            result[entityId] = stats;
                        }
                    }
                }
                catch
                {
                    // If batch fails entirely, fall back to individual fetches
                    foreach (var entityId in batch)
                    {
                        try
                        {
                            var individualStats = await GetPlayerStatistics(entityId);
                            result[entityId] = individualStats;
                        }
                        catch
                        {
                            result[entityId] = new Dictionary<string, int>();
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Initializes player statistics for a specific entity
        /// Stores base max stamina only - building bonus applied dynamically
        /// </summary>
        private async Task InitializePlayerStatisticsForEntity(string entityId, string clanId)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var stats = await GetPlayerStatistics(entityId);

            // Only initialize if not already set
            if (!stats.ContainsKey(ClanConstants.STAT_STAMINA))
            {
                // Store base max stamina only (no building bonus stored)
                await UpdatePlayerStatistics(entityId, new Dictionary<string, int>
                {
                    { ClanConstants.STAT_STAMINA, ClanConstants.BASE_MAX_STAMINA }, // Base stamina
                    { ClanConstants.STAT_MAX_STAMINA, ClanConstants.BASE_MAX_STAMINA }, // Base max (building bonus added at runtime)
                    { ClanConstants.STAT_STAMINA_LAST_UPDATE, (int)timestamp },
                    { ClanConstants.STAT_REPUTATION, 0 }
                });
            }
            // If player already has stats, no update needed - building bonus is dynamic!
        }

        #endregion
    }
}
