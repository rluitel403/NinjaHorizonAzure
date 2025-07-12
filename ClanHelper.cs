using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ServerModels;
using Newtonsoft.Json;

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
        public int DefenderTotalStamina { get; set; }
        public double AttackSuccessChance { get; set; }
        public int ReputationGained { get; set; }
        public int StaminaReduced { get; set; }
        public DateTime AttackTime { get; set; }
    }

    public static class ClanHelper
    {
        public static async Task<ClanInfo> CreateClanAsync(PlayFabUtil playFabUtil, string clanName, string description)
        {
            var groupId = $"clan_{Guid.NewGuid():N}";

            // Create shared group for clan
            await playFabUtil.CreateSharedGroup(groupId);

            // Set clan metadata
            var clanData = new Dictionary<string, string>
            {
                { "Name", clanName },
                { "Description", description },
                { "CreatedDate", DateTime.UtcNow.ToString("O") },
                { "CreatedBy", playFabUtil.Entity.Id },
                { "MemberCount", "1" },
                { "Members", JsonConvert.SerializeObject(new List<string> { playFabUtil.Entity.Id }) }
            };

            await playFabUtil.UpdateSharedGroupData(groupId, clanData);

            // Add creator to database
            var clanMember = new ClanMember
            {
                PlayerEntityKeyId = playFabUtil.Entity.Id,
                ClanId = groupId,
                Reputation = 0,
                Stamina = 100,
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
                Members = new List<string> { playFabUtil.Entity.Id }
            };
        }

        public static async Task<List<ClanInfo>> GetAllClansAsync(PlayFabUtil playFabUtil)
        {
            // For this simplified version, we'll get clans from the database
            // In a real implementation, you might want to maintain a global clan registry
            var clans = new List<ClanInfo>();

            // This is a simplified approach - in production you'd want a better way to track all clans
            // For now, we'll return an empty list and suggest using GetClanInfo for specific clans
            return clans;
        }

        public static async Task<ClanInfo> GetClanInfoAsync(PlayFabUtil playFabUtil, string groupId)
        {
            var sharedGroupData = await playFabUtil.GetSharedGroupData(groupId);

            if (sharedGroupData.Data == null || !sharedGroupData.Data.ContainsKey("Name"))
            {
                throw new Exception("Clan not found or invalid clan data");
            }

            var clanInfo = new ClanInfo
            {
                GroupId = groupId,
                GroupName = sharedGroupData.Data.ContainsKey("Name") ? sharedGroupData.Data["Name"].Value : "Unknown",
                Description = sharedGroupData.Data.ContainsKey("Description") ? sharedGroupData.Data["Description"].Value : "",
                CreatedDate = sharedGroupData.Data.ContainsKey("CreatedDate") ? DateTime.Parse(sharedGroupData.Data["CreatedDate"].Value) : DateTime.MinValue,
                MemberCount = sharedGroupData.Data.ContainsKey("MemberCount") ? int.Parse(sharedGroupData.Data["MemberCount"].Value) : 0
            };

            if (sharedGroupData.Data.ContainsKey("Members"))
            {
                clanInfo.Members = JsonConvert.DeserializeObject<List<string>>(sharedGroupData.Data["Members"].Value);
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

            // Update shared group data
            var updateData = new Dictionary<string, string>
            {
                { "Members", JsonConvert.SerializeObject(clanInfo.Members) },
                { "MemberCount", clanInfo.MemberCount.ToString() }
            };

            await playFabUtil.UpdateSharedGroupData(groupId, updateData);

            // Add to database
            var clanMember = new ClanMember
            {
                PlayerEntityKeyId = playFabUtil.Entity.Id,
                ClanId = groupId,
                Reputation = 0,
                Stamina = 100,
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

            // Update shared group data
            var updateData = new Dictionary<string, string>
            {
                { "Members", JsonConvert.SerializeObject(clanInfo.Members) },
                { "MemberCount", clanInfo.MemberCount.ToString() }
            };

            await playFabUtil.UpdateSharedGroupData(groupId, updateData);

            // Remove from database
            await DatabaseUtil.DeleteClanMemberAsync(playFabUtil.Entity.Id, groupId);
        }

        public static async Task InvitePlayerAsync(PlayFabUtil playFabUtil, string groupId, string playerEntityKeyId)
        {
            // For this simplified version, we'll store invitations in shared group data
            var sharedGroupData = await playFabUtil.GetSharedGroupData(groupId);

            var invitations = new List<string>();
            if (sharedGroupData.Data.ContainsKey("Invitations"))
            {
                invitations = JsonConvert.DeserializeObject<List<string>>(sharedGroupData.Data["Invitations"].Value);
            }

            if (!invitations.Contains(playerEntityKeyId))
            {
                invitations.Add(playerEntityKeyId);

                var updateData = new Dictionary<string, string>
                {
                    { "Invitations", JsonConvert.SerializeObject(invitations) }
                };

                await playFabUtil.UpdateSharedGroupData(groupId, updateData);
            }
        }

        public static async Task<List<ClanApplication>> GetClanApplicationsAsync(PlayFabUtil playFabUtil, string groupId)
        {
            var sharedGroupData = await playFabUtil.GetSharedGroupData(groupId);
            var applications = new List<ClanApplication>();

            if (sharedGroupData.Data.ContainsKey("Applications"))
            {
                var applicationData = JsonConvert.DeserializeObject<List<string>>(sharedGroupData.Data["Applications"].Value);
                foreach (var playerEntityKeyId in applicationData)
                {
                    applications.Add(new ClanApplication
                    {
                        GroupId = groupId,
                        PlayerEntityKeyId = playerEntityKeyId,
                        ApplicationDate = DateTime.UtcNow // Simplified - in production you'd store the actual date
                    });
                }
            }

            return applications;
        }

        public static async Task AcceptApplicationAsync(PlayFabUtil playFabUtil, string groupId, string entityKeyId)
        {
            // Remove from applications
            var sharedGroupData = await playFabUtil.GetSharedGroupData(groupId);
            var applications = new List<string>();

            if (sharedGroupData.Data.ContainsKey("Applications"))
            {
                applications = JsonConvert.DeserializeObject<List<string>>(sharedGroupData.Data["Applications"].Value);
                applications.Remove(entityKeyId);
            }

            // Add to members
            var members = new List<string>();
            if (sharedGroupData.Data.ContainsKey("Members"))
            {
                members = JsonConvert.DeserializeObject<List<string>>(sharedGroupData.Data["Members"].Value);
            }

            if (!members.Contains(entityKeyId))
            {
                members.Add(entityKeyId);

                var updateData = new Dictionary<string, string>
                {
                    { "Applications", JsonConvert.SerializeObject(applications) },
                    { "Members", JsonConvert.SerializeObject(members) },
                    { "MemberCount", members.Count.ToString() }
                };

                await playFabUtil.UpdateSharedGroupData(groupId, updateData);

                // Add to database
                var clanMember = new ClanMember
                {
                    PlayerEntityKeyId = entityKeyId,
                    ClanId = groupId,
                    Reputation = 0,
                    Stamina = 100,
                    JoinedDate = DateTime.UtcNow,
                    LastStaminaRestore = DateTime.UtcNow
                };

                await DatabaseUtil.InsertClanMemberAsync(clanMember);
            }
        }

        public static async Task RejectApplicationAsync(PlayFabUtil playFabUtil, string groupId, string entityKeyId)
        {
            var sharedGroupData = await playFabUtil.GetSharedGroupData(groupId);
            var applications = new List<string>();

            if (sharedGroupData.Data.ContainsKey("Applications"))
            {
                applications = JsonConvert.DeserializeObject<List<string>>(sharedGroupData.Data["Applications"].Value);
                applications.Remove(entityKeyId);

                var updateData = new Dictionary<string, string>
                {
                    { "Applications", JsonConvert.SerializeObject(applications) }
                };

                await playFabUtil.UpdateSharedGroupData(groupId, updateData);
            }
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
            var sharedGroupData = await playFabUtil.GetSharedGroupData(groupId);

            var applications = new List<string>();
            if (sharedGroupData.Data.ContainsKey("Applications"))
            {
                applications = JsonConvert.DeserializeObject<List<string>>(sharedGroupData.Data["Applications"].Value);
            }

            if (!applications.Contains(playFabUtil.Entity.Id))
            {
                applications.Add(playFabUtil.Entity.Id);

                var updateData = new Dictionary<string, string>
                {
                    { "Applications", JsonConvert.SerializeObject(applications) }
                };

                await playFabUtil.UpdateSharedGroupData(groupId, updateData);
            }
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

            clanMember.Stamina = Math.Max(0, Math.Min(100, clanMember.Stamina + staminaChange));
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

        public static async Task<List<ClanMember>> GetPlayerClanMembershipsAsync(PlayFabUtil playFabUtil)
        {
            return await DatabaseUtil.GetPlayerClanMembershipsAsync(playFabUtil.Entity.Id);
        }

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

            // Get clan members to calculate total stamina
            var defenderMembers = await DatabaseUtil.GetClanMembersAsync(defenderClanId);
            var totalDefenderStamina = defenderMembers.Sum(m => m.Stamina);

            // Calculate attack success chance based on defender's total stamina
            var attackSuccessChance = CalculateAttackSuccessChance(totalDefenderStamina);

            // Determine if attack succeeds
            var random = new Random();
            var isAttackSuccessful = random.NextDouble() < attackSuccessChance;

            var result = new ClanAttackResult
            {
                AttackerClanId = attackerClanId,
                DefenderClanId = defenderClanId,
                AttackerPlayerId = playFabUtil.Entity.Id,
                IsSuccessful = isAttackSuccessful,
                DefenderTotalStamina = totalDefenderStamina,
                AttackSuccessChance = attackSuccessChance,
                AttackTime = DateTime.UtcNow
            };

            if (isAttackSuccessful)
            {
                // Calculate reputation reward based on clan rankings
                var reputationReward = CalculateReputationReward(attackerClan.TotalReputation, defenderClan.TotalReputation);
                result.ReputationGained = reputationReward;

                // Award reputation to attacker
                await UpdateClanMemberReputationAsync(playFabUtil, attackerClanId, playFabUtil.Entity.Id, reputationReward);

                // Weaken the defending clan (reduce stamina)
                await WeakenDefendingClanAsync(defenderClanId, defenderMembers);
                result.StaminaReduced = CalculateStaminaReduction(totalDefenderStamina);
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

        private static async Task WeakenDefendingClanAsync(string defenderClanId, List<ClanMember> defenderMembers)
        {
            var totalStamina = defenderMembers.Sum(m => m.Stamina);
            var staminaReduction = CalculateStaminaReduction(totalStamina);

            // Update all members' stamina in a single database call
            await DatabaseUtil.ReduceClanStaminaAsync(defenderClanId, staminaReduction);
        }

        private static int CalculateStaminaReduction(int totalStamina)
        {
            // Fixed stamina reduction per attack to ensure 100 attacks fully weaken a clan
            // This ensures 25 members attacking 4 times each will fully weaken any clan
            return ClanConfig.STAMINA_REDUCTION_PER_ATTACK;
        }
    }
}