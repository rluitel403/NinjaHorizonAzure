using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;

namespace NinjaHorizon.Function
{
    public class ClanMember
    {
        public string PlayerEntityKeyId { get; set; }
        public string ClanId { get; set; }
        public int Reputation { get; set; }
        public int Stamina { get; set; }
        public DateTime JoinedDate { get; set; }
        public DateTime LastStaminaRestore { get; set; }
        public DateTime? LastAttackTime { get; set; }
    }

    public class ClanBuildings
    {
        public string ClanId { get; set; }
        public string ClanName { get; set; }
        public int TeaHouseLevel { get; set; }
        public int BathHouseLevel { get; set; }
        public int TrainingCentreLevel { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastBuildingUpgrade { get; set; }
    }

    public class ClanAttackLog
    {
        public long Id { get; set; }
        public string AttackerPlayerId { get; set; }
        public string AttackerClanId { get; set; }
        public string DefenderClanId { get; set; }
        public int AttackDamage { get; set; }
        public DateTime AttackTime { get; set; }
        public long TimeWindow { get; set; }
        public bool IsSuccessful { get; set; }
        public int AttackerTeaHouseLevel { get; set; }
        public int AttackerTrainingCentreLevel { get; set; }
    }

    public class ClanEffectiveStamina
    {
        public int BaseStamina { get; set; }
        public int SuccessfulAttacksReceived { get; set; }
        public int EffectiveStamina { get; set; }
        public long CurrentTimeWindow { get; set; }
        public int TotalAttacksReceived { get; set; }
    }

    public static class DatabaseUtil
    {
        private static string GetConnectionString()
        {
            return Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? throw new InvalidOperationException("SqlConnectionString environment variable is not set");
        }

        public static async Task<SqlConnection> GetConnectionAsync()
        {
            var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();
            return connection;
        }

        // Clan Buildings Methods
        public static async Task<ClanBuildings> GetClanBuildingsAsync(string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT ClanId, ClanName, TeaHouseLevel, BathHouseLevel, TrainingCentreLevel, CreatedDate, LastBuildingUpgrade 
                FROM Clans 
                WHERE ClanId = @ClanId";

            return await connection.QueryFirstOrDefaultAsync<ClanBuildings>(sql, new { ClanId = clanId });
        }

        public static async Task InsertClanBuildingsAsync(ClanBuildings clanBuildings)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                INSERT INTO Clans (ClanId, ClanName, TeaHouseLevel, BathHouseLevel, TrainingCentreLevel, CreatedDate, LastBuildingUpgrade)
                VALUES (@ClanId, @ClanName, @TeaHouseLevel, @BathHouseLevel, @TrainingCentreLevel, @CreatedDate, @LastBuildingUpgrade)";

            await connection.ExecuteAsync(sql, clanBuildings);
        }

        public static async Task<bool> UpgradeBuildingAsync(string clanId, string buildingType)
        {
            using var connection = await GetConnectionAsync();

            var sql = buildingType.ToLower() switch
            {
                "teahouse" => @"
                    UPDATE Clans 
                    SET TeaHouseLevel = TeaHouseLevel + 1, LastBuildingUpgrade = GETUTCDATE()
                    WHERE ClanId = @ClanId AND TeaHouseLevel < 5",
                "bathhouse" => @"
                    UPDATE Clans 
                    SET BathHouseLevel = BathHouseLevel + 1, LastBuildingUpgrade = GETUTCDATE()
                    WHERE ClanId = @ClanId AND BathHouseLevel < 5",
                "trainingcentre" => @"
                    UPDATE Clans 
                    SET TrainingCentreLevel = TrainingCentreLevel + 1, LastBuildingUpgrade = GETUTCDATE()
                    WHERE ClanId = @ClanId AND TrainingCentreLevel < 5",
                _ => throw new ArgumentException("Invalid building type")
            };

            var rowsAffected = await connection.ExecuteAsync(sql, new { ClanId = clanId });
            return rowsAffected > 0;
        }

        // Attack Logging Methods
        public static async Task<bool> LogAttackAsync(ClanAttackLog attackLog)
        {
            using var connection = await GetConnectionAsync();

            try
            {
                var sql = @"
                    INSERT INTO ClanAttackLogs 
                    (AttackerPlayerId, AttackerClanId, DefenderClanId, AttackDamage, AttackTime, TimeWindow, IsSuccessful, AttackerTeaHouseLevel, AttackerTrainingCentreLevel)
                    VALUES 
                    (@AttackerPlayerId, @AttackerClanId, @DefenderClanId, @AttackDamage, @AttackTime, @TimeWindow, @IsSuccessful, @AttackerTeaHouseLevel, @AttackerTrainingCentreLevel)";

                await connection.ExecuteAsync(sql, attackLog);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Removed CanPlayerAttackAsync - no cooldown system

        public static async Task<ClanEffectiveStamina> GetClanEffectiveStaminaAsync(string clanId)
        {
            using var connection = await GetConnectionAsync();
            var currentWindow = ClanConfig.GetCurrentTimeWindow();

            var sql = @"
                SELECT 
                    ISNULL(SUM(cm.Stamina), 0) as BaseStamina,
                    COUNT(cm.PlayerEntityKeyId) as MemberCount,
                    ISNULL((SELECT COUNT(*) 
                            FROM ClanAttackLogs 
                            WHERE DefenderClanId = @ClanId 
                            AND TimeWindow = @CurrentWindow 
                            AND IsSuccessful = 1), 0) as SuccessfulAttacksReceived,
                    ISNULL((SELECT COUNT(*) 
                            FROM ClanAttackLogs 
                            WHERE DefenderClanId = @ClanId 
                            AND TimeWindow = @CurrentWindow), 0) as TotalAttacksReceived
                FROM ClanMembers cm 
                LEFT JOIN Clans c ON cm.ClanId = c.ClanId
                WHERE cm.ClanId = @ClanId";

            var result = await connection.QueryFirstOrDefaultAsync(sql, new
            {
                ClanId = clanId,
                CurrentWindow = currentWindow
            });

            // Calculate dynamic weakening based on clan size and successful attack count
            var attackWeakeningRatio = ClanConfig.GetAttackWeakeningRatio(result.MemberCount);
            var weakeningRatio = Math.Min(1.0, result.SuccessfulAttacksReceived * attackWeakeningRatio);
            var effectiveStamina = (int)(result.BaseStamina * (1.0 - weakeningRatio));

            return new ClanEffectiveStamina
            {
                BaseStamina = result.BaseStamina,
                SuccessfulAttacksReceived = result.SuccessfulAttacksReceived,
                EffectiveStamina = effectiveStamina,
                CurrentTimeWindow = currentWindow,
                TotalAttacksReceived = result.TotalAttacksReceived
            };
        }

        public static async Task<List<ClanAttackLog>> GetRecentAttacksAsync(string clanId, int windowsBack = 1)
        {
            using var connection = await GetConnectionAsync();
            var currentWindow = ClanConfig.GetCurrentTimeWindow();

            var sql = @"
                SELECT * FROM ClanAttackLogs 
                WHERE DefenderClanId = @ClanId 
                AND TimeWindow >= @StartWindow 
                ORDER BY AttackTime DESC";

            var result = await connection.QueryAsync<ClanAttackLog>(sql, new
            {
                ClanId = clanId,
                StartWindow = currentWindow - windowsBack
            });

            return result.AsList();
        }

        // Update existing member management to consider building bonuses
        public static async Task<ClanMember> GetClanMemberAsync(string playerEntityKeyId, string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT PlayerEntityKeyId, ClanId, Reputation, Stamina, JoinedDate, LastStaminaRestore, LastAttackTime 
                FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            return await connection.QueryFirstOrDefaultAsync<ClanMember>(sql, new { PlayerEntityKeyId = playerEntityKeyId, ClanId = clanId });
        }

        public static async Task<List<ClanMember>> GetClanMembersAsync(string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT PlayerEntityKeyId, ClanId, Reputation, Stamina, JoinedDate, LastStaminaRestore, LastAttackTime 
                FROM ClanMembers 
                WHERE ClanId = @ClanId 
                ORDER BY Reputation DESC";

            var result = await connection.QueryAsync<ClanMember>(sql, new { ClanId = clanId });
            return result.AsList();
        }

        public static async Task<bool> InsertClanMemberAsync(ClanMember clanMember)
        {
            using var connection = await GetConnectionAsync();

            // Check if player is already in any clan
            var isAlreadyInClan = await IsPlayerInAnyClanAsync(clanMember.PlayerEntityKeyId);
            if (isAlreadyInClan)
            {
                return false; // Player is already in a clan
            }

            try
            {
                var sql = @"
                    INSERT INTO ClanMembers (PlayerEntityKeyId, ClanId, Reputation, Stamina, JoinedDate, LastStaminaRestore, LastAttackTime)
                    VALUES (@PlayerEntityKeyId, @ClanId, @Reputation, @Stamina, @JoinedDate, @LastStaminaRestore, @LastAttackTime)";

                await connection.ExecuteAsync(sql, clanMember);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task UpdateClanMemberAsync(ClanMember clanMember)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                UPDATE ClanMembers 
                SET Reputation = @Reputation, Stamina = @Stamina, LastStaminaRestore = @LastStaminaRestore, LastAttackTime = @LastAttackTime
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            await connection.ExecuteAsync(sql, clanMember);
        }

        public static async Task DeleteClanMemberAsync(string playerEntityKeyId, string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                DELETE FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            await connection.ExecuteAsync(sql, new { PlayerEntityKeyId = playerEntityKeyId, ClanId = clanId });
        }

        public static async Task<int> GetClanTotalReputationAsync(string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT ISNULL(SUM(Reputation), 0) 
                FROM ClanMembers 
                WHERE ClanId = @ClanId";

            return await connection.QueryFirstOrDefaultAsync<int>(sql, new { ClanId = clanId });
        }

        public static async Task<bool> IsPlayerInClanAsync(string playerEntityKeyId, string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT COUNT(1) 
                FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { PlayerEntityKeyId = playerEntityKeyId, ClanId = clanId });
            return count > 0;
        }

        public static async Task RestoreStaminaManuallyAsync(string playerEntityKeyId, string clanId, int staminaAmount)
        {
            using var connection = await GetConnectionAsync();

            // Get clan's bathhouse level to determine max stamina
            var clanBuildings = await GetClanBuildingsAsync(clanId);
            var maxStamina = ClanConfig.GetMaxStaminaForPlayer(clanBuildings?.BathHouseLevel ?? 1);

            var sql = $@"
                UPDATE ClanMembers 
                SET Stamina = CASE 
                    WHEN Stamina + @StaminaAmount > @MaxStamina THEN @MaxStamina 
                    ELSE Stamina + @StaminaAmount 
                END,
                LastStaminaRestore = GETUTCDATE()
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId";

            await connection.ExecuteAsync(sql, new
            {
                PlayerEntityKeyId = playerEntityKeyId,
                ClanId = clanId,
                StaminaAmount = staminaAmount,
                MaxStamina = maxStamina
            });
        }

        public static async Task<bool> RestoreStaminaIfNeededAsync(string playerEntityKeyId)
        {
            using var connection = await GetConnectionAsync();

            // Get the player's single clan membership
            var membership = await GetPlayerClanAsync(playerEntityKeyId);
            if (membership == null)
                return false;

            var clanBuildings = await GetClanBuildingsAsync(membership.ClanId);
            var maxStamina = ClanConfig.GetMaxStaminaForPlayer(clanBuildings?.BathHouseLevel ?? 1);

            var sql = $@"
                UPDATE ClanMembers 
                SET Stamina = CASE 
                    WHEN DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) >= @RestoreInterval AND Stamina < @MaxStamina THEN
                        CASE 
                            WHEN Stamina + (DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) / @RestoreInterval) * @RestoreAmount > @MaxStamina THEN @MaxStamina
                            ELSE Stamina + (DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) / @RestoreInterval) * @RestoreAmount
                        END
                    ELSE Stamina
                END,
                LastStaminaRestore = CASE 
                    WHEN DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) >= @RestoreInterval AND Stamina < @MaxStamina THEN GETUTCDATE()
                    ELSE LastStaminaRestore
                END
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId AND ClanId = @ClanId
                AND (DATEDIFF(MINUTE, LastStaminaRestore, GETUTCDATE()) >= @RestoreInterval AND Stamina < @MaxStamina)";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                PlayerEntityKeyId = playerEntityKeyId,
                ClanId = membership.ClanId,
                RestoreInterval = ClanConfig.AUTO_RESTORE_INTERVAL_MINUTES,
                RestoreAmount = ClanConfig.AUTO_RESTORE_STAMINA_AMOUNT,
                MaxStamina = maxStamina
            });

            return rowsAffected > 0;
        }

        public static async Task<ClanMember> GetPlayerClanAsync(string playerEntityKeyId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT PlayerEntityKeyId, ClanId, Reputation, Stamina, JoinedDate, LastStaminaRestore, LastAttackTime 
                FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId";

            return await connection.QueryFirstOrDefaultAsync<ClanMember>(sql, new { PlayerEntityKeyId = playerEntityKeyId });
        }


        public static async Task<bool> IsPlayerInAnyClanAsync(string playerEntityKeyId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT COUNT(1) 
                FROM ClanMembers 
                WHERE PlayerEntityKeyId = @PlayerEntityKeyId";

            var count = await connection.QueryFirstOrDefaultAsync<int>(sql, new { PlayerEntityKeyId = playerEntityKeyId });
            return count > 0;
        }

        public static async Task<List<ClanBuildings>> GetAllClansAsync(int limit = 100)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT TOP(@Limit) c.ClanId, c.ClanName, c.TeaHouseLevel, c.BathHouseLevel, c.TrainingCentreLevel, c.CreatedDate, c.LastBuildingUpgrade,
                       ISNULL(SUM(cm.Reputation), 0) as TotalReputation
                FROM Clans c
                LEFT JOIN ClanMembers cm ON c.ClanId = cm.ClanId
                GROUP BY c.ClanId, c.ClanName, c.TeaHouseLevel, c.BathHouseLevel, c.TrainingCentreLevel, c.CreatedDate, c.LastBuildingUpgrade
                ORDER BY ISNULL(SUM(cm.Reputation), 0) DESC";

            var result = await connection.QueryAsync<ClanBuildings>(sql, new { Limit = limit });
            return result.AsList();
        }

        public static async Task<int> GetClanMemberCountAsync(string clanId)
        {
            using var connection = await GetConnectionAsync();

            var sql = @"
                SELECT COUNT(*) 
                FROM ClanMembers 
                WHERE ClanId = @ClanId";

            return await connection.QueryFirstOrDefaultAsync<int>(sql, new { ClanId = clanId });
        }
    }
}