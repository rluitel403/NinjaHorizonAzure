-- Create ClanMembers table for Ninja Horizon Azure Functions
-- Run this script in your Azure SQL Database

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
    PRIMARY KEY (PlayerEntityKeyId, ClanId)
);

-- Optional: Create indexes for better query performance
CREATE INDEX IX_ClanMembers_ClanId ON ClanMembers(ClanId);
CREATE INDEX IX_ClanMembers_PlayerEntityKeyId ON ClanMembers(PlayerEntityKeyId);

-- Verify the table was created successfully
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'ClanMembers'
ORDER BY ORDINAL_POSITION; 