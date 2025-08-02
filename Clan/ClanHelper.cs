using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ServerModels;
using Newtonsoft.Json;
using EntityKey = PlayFab.EconomyModels.EntityKey;

namespace NinjaHorizon.Function
{
    public class ClanInfo
    {
        public string GroupId { get; set; }
        public string GroupName { get; set; }
        public string Description { get; set; }
        public DateTime CreatedDate { get; set; }
        public int MemberCount { get; set; }
        public int TotalReputation { get; set; }
        public List<string> Members { get; set; } = new List<string>();
        public ClanBuildings Buildings { get; set; }
    }

    public class ClanApplication
    {
        public string GroupId { get; set; }
        public string PlayerEntityKeyId { get; set; }
        public DateTime ApplicationDate { get; set; }
    }

    public class ClanAttackResult
    {
        public string AttackerClanId { get; set; }
        public string DefenderClanId { get; set; }
        public string AttackerPlayerId { get; set; }
        public bool IsSuccessful { get; set; }
        public int DefenderEffectiveStamina { get; set; }
        public double AttackSuccessChance { get; set; }
        public int ReputationGained { get; set; }
        public int StaminaReduced { get; set; }
        public DateTime AttackTime { get; set; }
        public ClanBuildings AttackerBuildings { get; set; }
        public ClanBuildings DefenderBuildings { get; set; }
    }

    public static class ClanHelper
    {
        public static async Task<ClanInfo> CreateClanAsync(PlayFabUtil playFabUtil, string clanName, string description)
        {
            // Create entity group for clan
            var createGroupResponse = await playFabUtil.CreateEntityGroup(clanName, playFabUtil.Entity);
            var groupId = createGroupResponse.Group.Id;

            // Set clan metadata using entity group objects
            var clanData = new Dictionary<string, object>
            {
                { "ClanInfo", new
                    {
                        Name = clanName,
                        Description = description,
                        CreatedDate = DateTime.UtcNow.ToString("O"),
                        CreatedBy = playFabUtil.Entity.Id,
                        MemberCount = 1,
                        Members = new List<string> { playFabUtil.Entity.Id }
                    }
                }
            };

            await playFabUtil.SetEntityGroupObjects(groupId, clanData);

            // Create clan buildings entry with default levels
            var clanBuildings = new ClanBuildings
            {
                ClanId = groupId,
                ClanName = clanName,
                TeaHouseLevel = 1,
                BathHouseLevel = 1,
                TrainingCentreLevel = 1,
                CreatedDate = DateTime.UtcNow,
                LastBuildingUpgrade = DateTime.UtcNow
            };

            await DatabaseUtil.InsertClanBuildingsAsync(clanBuildings);

            // Add creator to database with proper max stamina based on bathhouse level
            var maxStamina = ClanConfig.GetMaxStaminaForPlayer(clanBuildings.BathHouseLevel);
            var clanMember = new ClanMember
            {
                PlayerEntityKeyId = playFabUtil.Entity.Id,
                ClanId = groupId,
                Reputation = 0,
                Stamina = maxStamina,
                JoinedDate = DateTime.UtcNow,
                LastStaminaRestore = DateTime.UtcNow
            };

            await DatabaseUtil.InsertClanMemberAsync(clanMember);

            return new ClanInfo
            {
                GroupId = groupId,
                GroupName = clanName,
                Description = description,
                CreatedDate = DateTime.UtcNow,
                MemberCount = 1,
                TotalReputation = 0,
                Members = new List<string> { playFabUtil.Entity.Id },
                Buildings = clanBuildings
            };
        }

        public static async Task<List<ClanInfo>> GetAllClansAsync(PlayFabUtil playFabUtil)
        {
            var clans = new List<ClanInfo>();

            try
            {
                // Get all clans from database (up to 100)
                var allClans = await DatabaseUtil.GetAllClansAsync(100);

                foreach (var clanBuildings in allClans)
                {
                    var memberCount = await DatabaseUtil.GetClanMemberCountAsync(clanBuildings.ClanId);
                    var totalReputation = await DatabaseUtil.GetClanTotalReputationAsync(clanBuildings.ClanId);

                    var clanInfo = new ClanInfo
                    {
                        GroupId = clanBuildings.ClanId,
                        GroupName = clanBuildings.ClanName,
                        Description = "",
                        CreatedDate = clanBuildings.CreatedDate,
                        MemberCount = memberCount,
                        TotalReputation = totalReputation,
                        Members = new List<string>(),
                        Buildings = clanBuildings
                    };
                    clans.Add(clanInfo);
                }
            }
            catch (Exception)
            {
                // Return empty list if there's an error
            }

            return clans;
        }

        public static async Task<ClanInfo> GetClanInfoAsync(PlayFabUtil playFabUtil, string groupId)
        {
            var groupObjectsResponse = await playFabUtil.GetEntityGroupObjects(groupId, new List<string> { "ClanInfo" });

            if (groupObjectsResponse.Objects == null || !groupObjectsResponse.Objects.ContainsKey("ClanInfo"))
            {
                throw new Exception("Clan not found or invalid clan data");
            }

            var clanInfoObject = JsonConvert.DeserializeObject<dynamic>(groupObjectsResponse.Objects["ClanInfo"].DataObject.ToString());

            var clanInfo = new ClanInfo
            {
                GroupId = groupId,
                GroupName = clanInfoObject.Name ?? "Unknown",
                Description = clanInfoObject.Description ?? "",
                CreatedDate = clanInfoObject.CreatedDate != null ? DateTime.Parse(clanInfoObject.CreatedDate.ToString()) : DateTime.MinValue,
                MemberCount = clanInfoObject.MemberCount ?? 0
            };

            if (clanInfoObject.Members != null)
            {
                clanInfo.Members = JsonConvert.DeserializeObject<List<string>>(clanInfoObject.Members.ToString());
            }

            // Get clan buildings
            try
            {
                clanInfo.Buildings = await DatabaseUtil.GetClanBuildingsAsync(groupId);
            }
            catch (Exception)
            {
                // Create default buildings if they don't exist
                clanInfo.Buildings = new ClanBuildings
                {
                    ClanId = groupId,
                    ClanName = clanInfo.GroupName,
                    TeaHouseLevel = 1,
                    BathHouseLevel = 1,
                    TrainingCentreLevel = 1,
                    CreatedDate = DateTime.UtcNow,
                    LastBuildingUpgrade = DateTime.UtcNow
                };
                await DatabaseUtil.InsertClanBuildingsAsync(clanInfo.Buildings);
            }

            // Get total reputation from database
            try
            {
                clanInfo.TotalReputation = await DatabaseUtil.GetClanTotalReputationAsync(groupId);
            }
            catch (Exception)
            {
                clanInfo.TotalReputation = 0;
            }

            return clanInfo;
        }

        public static async Task JoinClanAsync(PlayFabUtil playFabUtil, string groupId)
        {
            var clanInfo = await GetClanInfoAsync(playFabUtil, groupId);

            if (clanInfo.Members.Contains(playFabUtil.Entity.Id))
            {
                throw new Exception("Player is already a member of this clan");
            }

            // Add player to members list
            clanInfo.Members.Add(playFabUtil.Entity.Id);
            clanInfo.MemberCount++;

            // Update entity group object data
            var updateData = new Dictionary<string, object>
            {
                { "ClanInfo", new
                    {
                        Name = clanInfo.GroupName,
                        Description = clanInfo.Description,
                        CreatedDate = clanInfo.CreatedDate.ToString("O"),
                        CreatedBy = clanInfo.Members.FirstOrDefault(), // Assuming first member is creator
                        MemberCount = clanInfo.MemberCount,
                        Members = clanInfo.Members
                    }
                }
            };

            await playFabUtil.SetEntityGroupObjects(groupId, updateData);

            // Add to database with proper max stamina based on bathhouse level
            var maxStamina = ClanConfig.GetMaxStaminaForPlayer(clanInfo.Buildings.BathHouseLevel);
            var clanMember = new ClanMember
            {
                PlayerEntityKeyId = playFabUtil.Entity.Id,
                ClanId = groupId,
                Reputation = 0,
                Stamina = maxStamina,
                JoinedDate = DateTime.UtcNow,
                LastStaminaRestore = DateTime.UtcNow
            };

            await DatabaseUtil.InsertClanMemberAsync(clanMember);
        }

        public static async Task LeaveClanAsync(PlayFabUtil playFabUtil, string groupId)
        {
            var clanInfo = await GetClanInfoAsync(playFabUtil, groupId);

            if (!clanInfo.Members.Contains(playFabUtil.Entity.Id))
            {
                throw new Exception("Player is not a member of this clan");
            }

            // Remove player from members list
            clanInfo.Members.Remove(playFabUtil.Entity.Id);
            clanInfo.MemberCount--;

            // Remove from entity group membership
            await playFabUtil.RemoveEntityGroupMembers(groupId, new List<EntityKey> { playFabUtil.Entity });

            // Update entity group object data
            var updateData = new Dictionary<string, object>
            {
                { "ClanInfo", new
                    {
                        Name = clanInfo.GroupName,
                        Description = clanInfo.Description,
                        CreatedDate = clanInfo.CreatedDate.ToString("O"),
                        CreatedBy = clanInfo.Members.FirstOrDefault(), // Assuming first member is creator
                        MemberCount = clanInfo.MemberCount,
                        Members = clanInfo.Members
                    }
                }
            };

            await playFabUtil.SetEntityGroupObjects(groupId, updateData);

            // Remove from database
            await DatabaseUtil.DeleteClanMemberAsync(playFabUtil.Entity.Id, groupId);
        }

        public static async Task InvitePlayerAsync(PlayFabUtil playFabUtil, string groupId, string playerEntityKeyId)
        {
            // Use entity group invitation system
            var playerEntity = new EntityKey
            {
                Id = playerEntityKeyId,
                Type = "title_player_account" // Assuming player entity type
            };

            await playFabUtil.InviteToEntityGroup(groupId, playerEntity);
        }

        public static async Task<List<ClanApplication>> GetClanApplicationsAsync(PlayFabUtil playFabUtil, string groupId)
        {
            // Use entity group application system
            var applicationsResponse = await playFabUtil.ListEntityGroupApplications(groupId);
            var applications = new List<ClanApplication>();

            foreach (var application in applicationsResponse.Applications)
            {
                applications.Add(new ClanApplication
                {
                    GroupId = groupId,
                    PlayerEntityKeyId = application.Entity.Key.Id,
                    ApplicationDate = application.Expires // Using expires as a proxy for application date
                });
            }

            return applications;
        }

        public static async Task AcceptApplicationAsync(PlayFabUtil playFabUtil, string groupId, string entityKeyId)
        {
            // Accept the application using entity group system
            var playerEntity = new EntityKey
            {
                Id = entityKeyId,
                Type = "title_player_account" // Assuming player entity type
            };

            await playFabUtil.AcceptEntityGroupApplication(groupId, playerEntity);

            // Update clan info object
            var clanInfo = await GetClanInfoAsync(playFabUtil, groupId);

            if (!clanInfo.Members.Contains(entityKeyId))
            {
                clanInfo.Members.Add(entityKeyId);
                clanInfo.MemberCount++;

                var updateData = new Dictionary<string, object>
                {
                    { "ClanInfo", new
                        {
                            Name = clanInfo.GroupName,
                            Description = clanInfo.Description,
                            CreatedDate = clanInfo.CreatedDate.ToString("O"),
                            CreatedBy = clanInfo.Members.FirstOrDefault(), // Assuming first member is creator
                            MemberCount = clanInfo.MemberCount,
                            Members = clanInfo.Members
                        }
                    }
                };

                await playFabUtil.SetEntityGroupObjects(groupId, updateData);

                // Add to database with proper max stamina
                var maxStamina = ClanConfig.GetMaxStaminaForPlayer(clanInfo.Buildings.BathHouseLevel);
                var clanMember = new ClanMember
                {
                    PlayerEntityKeyId = entityKeyId,
                    ClanId = groupId,
                    Reputation = 0,
                    Stamina = maxStamina,
                    JoinedDate = DateTime.UtcNow,
                    LastStaminaRestore = DateTime.UtcNow
                };

                await DatabaseUtil.InsertClanMemberAsync(clanMember);
            }
        }

        public static async Task RejectApplicationAsync(PlayFabUtil playFabUtil, string groupId, string entityKeyId)
        {
            // Reject the application using entity group system
            var playerEntity = new EntityKey
            {
                Id = entityKeyId,
                Type = "title_player_account" // Assuming player entity type
            };

            await playFabUtil.RejectEntityGroupApplication(groupId, playerEntity);
        }

        public static async Task<bool> IsPlayerInClanAsync(PlayFabUtil playFabUtil, string groupId)
        {
            try
            {
                var clanInfo = await GetClanInfoAsync(playFabUtil, groupId);
                return clanInfo.Members.Contains(playFabUtil.Entity.Id);
            }
            catch
            {
                return false;
            }
        }

        public static async Task ApplyToClanAsync(PlayFabUtil playFabUtil, string groupId)
        {
            // Use entity group application system
            await playFabUtil.ApplyToEntityGroup(groupId, playFabUtil.Entity);
        }

        public static async Task<List<ClanMember>> GetClanMembersAsync(PlayFabUtil playFabUtil, string groupId)
        {
            return await DatabaseUtil.GetClanMembersAsync(groupId);
        }

        public static async Task UpdateClanMemberReputationAsync(PlayFabUtil playFabUtil, string groupId, string playerEntityKeyId, int reputationChange)
        {
            var clanMember = await DatabaseUtil.GetClanMemberAsync(playerEntityKeyId, groupId);
            if (clanMember == null)
            {
                throw new Exception("Player is not a member of this clan");
            }

            clanMember.Reputation += reputationChange;
            await DatabaseUtil.UpdateClanMemberAsync(clanMember);
        }

        public static async Task UpdateClanMemberStaminaAsync(PlayFabUtil playFabUtil, string groupId, string playerEntityKeyId, int staminaChange)
        {
            var clanMember = await DatabaseUtil.GetClanMemberAsync(playerEntityKeyId, groupId);
            if (clanMember == null)
            {
                throw new Exception("Player is not a member of this clan");
            }

            // Get clan buildings to determine max stamina
            var clanBuildings = await DatabaseUtil.GetClanBuildingsAsync(groupId);
            var maxStamina = ClanConfig.GetMaxStaminaForPlayer(clanBuildings?.BathHouseLevel ?? 1);

            clanMember.Stamina = Math.Max(0, Math.Min(maxStamina, clanMember.Stamina + staminaChange));
            await DatabaseUtil.UpdateClanMemberAsync(clanMember);
        }

        public static async Task RestoreStaminaManuallyAsync(PlayFabUtil playFabUtil, string groupId, string playerEntityKeyId, int tokenCost = ClanConfig.MANUAL_RESTORE_TOKEN_COST, int staminaRestore = ClanConfig.MANUAL_RESTORE_STAMINA_AMOUNT)
        {
            // Check if player is in clan
            var clanMember = await DatabaseUtil.GetClanMemberAsync(playerEntityKeyId, groupId);
            if (clanMember == null)
            {
                throw new Exception("Player is not a member of this clan");
            }

            // Check if player has enough tokens using PlayFab
            var tokenItem = await playFabUtil.GetCurrencyItem(InventoryUtil.TOKEN_ID, tokenCost);
            if (tokenItem.Amount < tokenCost)
            {
                throw new Exception($"Not enough tokens. Required: {tokenCost}, Available: {tokenItem.Amount}");
            }

            // Deduct tokens
            var operations = new List<PlayFab.EconomyModels.InventoryOperation>
            {
                new PlayFab.EconomyModels.InventoryOperation
                {
                    Subtract = new PlayFab.EconomyModels.SubtractInventoryItemsOperation
                    {
                        Item = new PlayFab.EconomyModels.InventoryItemReference
                        {
                            Id = InventoryUtil.TOKEN_ID
                        },
                        Amount = tokenCost
                    }
                }
            };

            await playFabUtil.ExecuteInventoryOperations(operations);

            // Restore stamina
            await DatabaseUtil.RestoreStaminaManuallyAsync(playerEntityKeyId, groupId, staminaRestore);
        }

        public static async Task RestorePlayerStaminaIfNeededAsync(PlayFabUtil playFabUtil)
        {
            await DatabaseUtil.RestoreStaminaIfNeededAsync(playFabUtil.Entity.Id);
        }

        public static async Task<ClanMember> GetPlayerClanAsync(PlayFabUtil playFabUtil)
        {
            // We can keep using the database for detailed clan member info as requested
            // The entity group system handles membership, but detailed player stats stay in SQL
            return await DatabaseUtil.GetPlayerClanAsync(playFabUtil.Entity.Id);
        }

        // Building Management Methods
        public static async Task<bool> UpgradeBuildingAsync(PlayFabUtil playFabUtil, string clanId, string buildingType)
        {
            // Verify player is in clan
            if (!await IsPlayerInClanAsync(playFabUtil, clanId))
            {
                throw new Exception("Player is not a member of this clan");
            }

            // TODO: Add reputation cost check here
            // var upgradeCost = ClanConfig.BUILDING_UPGRADE_COSTS[currentLevel + 1];

            return await DatabaseUtil.UpgradeBuildingAsync(clanId, buildingType);
        }

        public static async Task<ClanEffectiveStamina> GetClanCurrentStatusAsync(PlayFabUtil playFabUtil, string groupId)
        {
            return await DatabaseUtil.GetClanEffectiveStaminaAsync(groupId);
        }

        public static async Task<List<ClanAttackLog>> GetClanRecentAttacksAsync(PlayFabUtil playFabUtil, string groupId)
        {
            return await DatabaseUtil.GetRecentAttacksAsync(groupId, 2); // Last 2 windows (60 minutes)
        }

        // Updated Attack System with Building Bonuses and Logging
        public static async Task<ClanAttackResult> AttackClanAsync(PlayFabUtil playFabUtil, string attackerClanId, string defenderClanId)
        {
            // Get clan information
            var attackerClan = await GetClanInfoAsync(playFabUtil, attackerClanId);
            var defenderClan = await GetClanInfoAsync(playFabUtil, defenderClanId);

            // Verify attacker is in the attacking clan
            if (!attackerClan.Members.Contains(playFabUtil.Entity.Id))
            {
                throw new Exception("Player is not a member of the attacking clan");
            }

            // Get defender's current effective stamina (base - recent attacks in current time window)
            var defenderEffectiveStamina = await DatabaseUtil.GetClanEffectiveStaminaAsync(defenderClanId);

            // Calculate attack success chance with building bonuses
            var baseSuccessChance = CalculateAttackSuccessChance(defenderEffectiveStamina.EffectiveStamina);
            var teaHouseBonus = ClanConfig.GetTeaHouseAttackBonus(attackerClan.Buildings.TeaHouseLevel);
            var attackSuccessChance = Math.Min(ClanConfig.MAX_ATTACK_SUCCESS_CHANCE, baseSuccessChance + teaHouseBonus);

            // Determine if attack succeeds
            var random = new Random();
            var isAttackSuccessful = random.NextDouble() < attackSuccessChance;

            // Calculate attack damage with training centre bonus
            var baseDamage = ClanConfig.BASE_STAMINA_REDUCTION_PER_ATTACK;
            var trainingCentreMultiplier = ClanConfig.GetTrainingCentreDamageMultiplier(attackerClan.Buildings.TrainingCentreLevel);
            var attackDamage = isAttackSuccessful ? (int)(baseDamage * trainingCentreMultiplier) : 0;

            var currentTimeWindow = ClanConfig.GetCurrentTimeWindow();

            // Log the attack
            var attackLog = new ClanAttackLog
            {
                AttackerPlayerId = playFabUtil.Entity.Id,
                AttackerClanId = attackerClanId,
                DefenderClanId = defenderClanId,
                AttackDamage = attackDamage,
                AttackTime = DateTime.UtcNow,
                TimeWindow = currentTimeWindow,
                IsSuccessful = isAttackSuccessful,
                AttackerTeaHouseLevel = attackerClan.Buildings.TeaHouseLevel,
                AttackerTrainingCentreLevel = attackerClan.Buildings.TrainingCentreLevel
            };

            var attackLogged = await DatabaseUtil.LogAttackAsync(attackLog);
            if (!attackLogged)
            {
                throw new Exception("Attack failed - database error");
            }

            var result = new ClanAttackResult
            {
                AttackerClanId = attackerClanId,
                DefenderClanId = defenderClanId,
                AttackerPlayerId = playFabUtil.Entity.Id,
                IsSuccessful = isAttackSuccessful,
                DefenderEffectiveStamina = defenderEffectiveStamina.EffectiveStamina,
                AttackSuccessChance = attackSuccessChance,
                AttackTime = DateTime.UtcNow,
                StaminaReduced = attackDamage,
                AttackerBuildings = attackerClan.Buildings,
                DefenderBuildings = defenderClan.Buildings
            };

            if (isAttackSuccessful)
            {
                // Calculate reputation reward based on clan rankings
                var reputationReward = CalculateReputationReward(attackerClan.TotalReputation, defenderClan.TotalReputation);
                result.ReputationGained = reputationReward;

                // Award reputation to attacker
                await UpdateClanMemberReputationAsync(playFabUtil, attackerClanId, playFabUtil.Entity.Id, reputationReward);
            }

            return result;
        }

        private static double CalculateAttackSuccessChance(int totalDefenderStamina)
        {
            // Formula: Higher stamina = lower chance of successful attack
            // Uses configurable values for consistency

            var staminaRatio = Math.Min(1.0, (double)totalDefenderStamina / ClanConfig.MAX_TOTAL_CLAN_STAMINA);
            var successChance = ClanConfig.BASE_ATTACK_SUCCESS_CHANCE -
                               (ClanConfig.BASE_ATTACK_SUCCESS_CHANCE - ClanConfig.MIN_ATTACK_SUCCESS_CHANCE) * staminaRatio;

            return Math.Max(ClanConfig.MIN_ATTACK_SUCCESS_CHANCE, successChance);
        }

        private static int CalculateReputationReward(int attackerReputation, int defenderReputation)
        {
            // Calculate rank difference using configurable values
            var attackerRank = attackerReputation / ClanConfig.REPUTATION_PER_1000_POINTS;
            var defenderRank = defenderReputation / ClanConfig.REPUTATION_PER_1000_POINTS;
            var rankDifference = defenderRank - attackerRank;

            if (rankDifference >= 1)
            {
                // Attacking higher rank clan - more reward
                var bonusReward = Math.Min(
                    ClanConfig.MAX_REPUTATION_REWARD - ClanConfig.MIN_REPUTATION_REWARD,
                    rankDifference * ClanConfig.REPUTATION_PER_RANK_DIFFERENCE
                );
                return ClanConfig.MIN_REPUTATION_REWARD + bonusReward;
            }
            else if (rankDifference == 0)
            {
                // Same rank
                return ClanConfig.MIN_REPUTATION_REWARD + 1;
            }
            else
            {
                // Attacking lower rank clan - base reward only
                return ClanConfig.MIN_REPUTATION_REWARD;
            }
        }

        // Effective stamina calculation: Base stamina - attack damage received in current 30-min window
        // Time windows reset every 30 minutes, providing natural "healing" for clans
    }
}