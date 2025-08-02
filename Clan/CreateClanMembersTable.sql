-- Create ClanMembers table for Ninja Horizon Azure Functions
-- Run this script in your Azure SQL Database

-- First, add LastAttackTime column to existing ClanMembers table (if it exists)
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ClanMembers')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ClanMembers' AND COLUMN_NAME = 'LastAttackTime')
    BEGIN
        ALTER TABLE ClanMembers ADD LastAttackTime DATETIME2 NULL;
    END
END
ELSE
BEGIN
    -- Create ClanMembers table if it doesn't exist
    CREATE TABLE ClanMembers (
        PlayerEntityKeyId NVARCHAR(255) NOT NULL,
        ClanId NVARCHAR(255) NOT NULL,
        Reputation INT NOT NULL DEFAULT 0,
        Stamina INT NOT NULL DEFAULT 100,
        AttackCount INT NOT NULL DEFAULT 0,
        AttackSuccessCount INT NOT NULL DEFAULT 0,
        TokenBurnedCount INT NOT NULL DEFAULT 0,
        JoinedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastStaminaRestore DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastAttackTime DATETIME2 NULL,
        PRIMARY KEY (PlayerEntityKeyId, ClanId)
    );
END

-- Create Clans table for building levels and clan stats
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Clans')
BEGIN
    CREATE TABLE Clans (
        ClanId NVARCHAR(255) PRIMARY KEY,
        ClanName NVARCHAR(255) NOT NULL,
        TeaHouseLevel INT NOT NULL DEFAULT 1,          -- Increases attack success chance
        BathHouseLevel INT NOT NULL DEFAULT 1,         -- Increases stamina capacity
        TrainingCentreLevel INT NOT NULL DEFAULT 1,    -- Increases attack damage/weakening
        CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastBuildingUpgrade DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT CK_TeaHouseLevel CHECK (TeaHouseLevel >= 1 AND TeaHouseLevel <= 5),
        CONSTRAINT CK_BathHouseLevel CHECK (BathHouseLevel >= 1 AND BathHouseLevel <= 5),
        CONSTRAINT CK_TrainingCentreLevel CHECK (TrainingCentreLevel >= 1 AND TrainingCentreLevel <= 5)
    );
END

-- Create ClanAttackLogs table for the new attack logging system
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ClanAttackLogs')
BEGIN
    CREATE TABLE ClanAttackLogs (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        AttackerPlayerId NVARCHAR(255) NOT NULL,
        AttackerClanId NVARCHAR(255) NOT NULL,
        DefenderClanId NVARCHAR(255) NOT NULL,
        AttackDamage INT NOT NULL,
        AttackTime DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        TimeWindow BIGINT NOT NULL,
        IsSuccessful BIT NOT NULL,
        AttackerTeaHouseLevel INT NOT NULL DEFAULT 1,    -- Store attacker's building levels at time of attack
        AttackerTrainingCentreLevel INT NOT NULL DEFAULT 1
    );
END

-- Create indexes for better query performance
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ClanMembers_ClanId')
    CREATE INDEX IX_ClanMembers_ClanId ON ClanMembers(ClanId);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ClanMembers_PlayerEntityKeyId')
    CREATE INDEX IX_ClanMembers_PlayerEntityKeyId ON ClanMembers(PlayerEntityKeyId);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Clans_ClanId')
    CREATE INDEX IX_Clans_ClanId ON Clans(ClanId);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ClanAttackLogs_DefenderClan_TimeWindow')
    CREATE INDEX IX_ClanAttackLogs_DefenderClan_TimeWindow ON ClanAttackLogs(DefenderClanId, TimeWindow);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ClanAttackLogs_AttackerPlayer_TimeWindow')
    CREATE INDEX IX_ClanAttackLogs_AttackerPlayer_TimeWindow ON ClanAttackLogs(AttackerPlayerId, TimeWindow);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ClanAttackLogs_TimeWindow')
    CREATE INDEX IX_ClanAttackLogs_TimeWindow ON ClanAttackLogs(TimeWindow);

-- Optional: Add cleanup job for old attack logs (older than 24 hours)
-- You can run this periodically to keep the table size manageable
/*
DELETE FROM ClanAttackLogs 
WHERE AttackTime < DATEADD(HOUR, -24, GETUTCDATE());
*/

-- Verify the tables were created successfully
SELECT 'ClanMembers' as TableName, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'ClanMembers'
UNION ALL
SELECT 'Clans' as TableName, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Clans'
UNION ALL
SELECT 'ClanAttackLogs' as TableName, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'ClanAttackLogs'
ORDER BY TableName, ORDINAL_POSITION; 