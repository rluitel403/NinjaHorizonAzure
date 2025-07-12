# Clan System Test Script

This document shows how to test the clan functionality using the Azure Function.

## Setup

1. **Database Connection**: Set the `SqlConnectionString` environment variable to your Azure SQL Database connection string.

2. **PlayFab Settings**: Set the `DeveloperSecretKey` environment variable to your PlayFab developer secret key.

## Test Cases

### 1. Create a Clan

```json
{
  "Action": "createclan",
  "ClanName": "Dragon Warriors",
  "Description": "A clan for the bravest warriors"
}
```

### 2. Get Clan Information

```json
{
  "Action": "getclaninfo",
  "GroupId": "clan_12345abcdef"
}
```

### 3. Join a Clan

```json
{
  "Action": "joinclan",
  "GroupId": "clan_12345abcdef"
}
```

### 4. Apply to a Clan

```json
{
  "Action": "applytoclan",
  "GroupId": "clan_12345abcdef"
}
```

### 5. Get Clan Applications (for clan leaders)

```json
{
  "Action": "getclanapplications",
  "GroupId": "clan_12345abcdef"
}
```

### 6. Accept an Application

```json
{
  "Action": "acceptapplication",
  "GroupId": "clan_12345abcdef",
  "PlayerEntityKeyId": "player_entity_key_id"
}
```

### 7. Reject an Application

```json
{
  "Action": "rejectapplication",
  "GroupId": "clan_12345abcdef",
  "PlayerEntityKeyId": "player_entity_key_id"
}
```

### 8. Invite a Player

```json
{
  "Action": "inviteplayer",
  "GroupId": "clan_12345abcdef",
  "PlayerEntityKeyId": "player_entity_key_id"
}
```

### 9. Get Clan Members

```json
{
  "Action": "getclanmembers",
  "GroupId": "clan_12345abcdef"
}
```

### 10. Update Player Reputation

```json
{
  "Action": "updatereputation",
  "GroupId": "clan_12345abcdef",
  "PlayerEntityKeyId": "player_entity_key_id",
  "ReputationChange": 10
}
```

### 11. Update Player Stamina

```json
{
  "Action": "updatestamina",
  "GroupId": "clan_12345abcdef",
  "PlayerEntityKeyId": "player_entity_key_id",
  "StaminaChange": -5
}
```

### 12. Check if Player is in Clan

```json
{
  "Action": "isplayerinclan",
  "GroupId": "clan_12345abcdef"
}
```

### 13. Leave a Clan

```json
{
  "Action": "leaveclan",
  "GroupId": "clan_12345abcdef"
}
```

### 14. Restore Stamina Manually (Costs Tokens)

```json
{
  "Action": "restorestamina",
  "GroupId": "clan_12345abcdef",
  "PlayerEntityKeyId": "player_entity_key_id",
  "TokenCost": 20,
  "StaminaRestore": 50
}
```

### 15. Get Player Clan Memberships (with updated stamina)

```json
{
  "Action": "getplayerclanmemberships"
}
```

### 16. Attack a Clan

```json
{
  "Action": "attackclan",
  "AttackerClanId": "clan_12345abcdef",
  "DefenderClanId": "clan_67890ghijkl"
}
```

## Database Schema

**IMPORTANT**: You must manually create the ClanMembers table in your Azure SQL Database before using the clan functions. Run the SQL script from `CreateClanMembersTable.sql`:

```sql
CREATE TABLE ClanMembers (
    PlayerEntityKeyId NVARCHAR(255) NOT NULL,
    ClanId NVARCHAR(255) NOT NULL,
    Reputation INT NOT NULL DEFAULT 0,
    Stamina INT NOT NULL DEFAULT 100,
    JoinedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastStaminaRestore DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    PRIMARY KEY (PlayerEntityKeyId, ClanId)
);
```

## Example Usage Flow

1. **Create a clan**: Use `createclan` action
2. **Other players apply**: Use `applytoclan` action
3. **Clan leader reviews applications**: Use `getclanapplications` action
4. **Accept/reject applications**: Use `acceptapplication` or `rejectapplication` actions
5. **Manage clan members**: Use `getclanmembers`, `updatereputation`, `updatestamina` actions
6. **Get clan statistics**: The total reputation is automatically calculated and returned

## Features

- **PlayFab Integration**: Uses PlayFab shared groups for clan data storage
- **Azure SQL Database**: Stores clan member statistics (reputation, stamina) for fast queries
- **Automatic Table Creation**: The database table is created automatically on first run
- **Comprehensive API**: Supports all common clan operations
- **Error Handling**: Proper error handling and logging throughout
- **Scalable Design**: Uses efficient database queries and PlayFab APIs
- **Automatic Stamina Restoration**: On-demand restoration when players interact with clan system
- **Manual Stamina Restoration**: Players can spend tokens to restore stamina immediately
- **Time-Based Stamina Tracking**: Tracks when stamina was last restored for each player

## Notes

- Clan IDs are generated automatically in the format `clan_[guid]`
- The system tracks both PlayFab group membership and database records
- Total clan reputation is calculated by summing all member reputations
- Stamina cannot go below 0 or above 100 when updated
- All operations are logged for debugging and monitoring

## Stamina System

### Automatic Restoration (On-Demand)
- **Trigger**: Every time a player calls any clan function
- **Restoration Logic**: Calculates stamina based on time passed since last restoration
- **Restoration Rate**: 50 stamina every 30 minutes
- **Maximum Stamina**: 100 (stamina cannot exceed this limit)
- **Smart Calculation**: Accumulates multiple 30-minute periods if player was offline longer

### Manual Restoration
- **Cost**: 20 tokens (configurable via `TokenCost` parameter)
- **Restoration Amount**: 50 stamina (configurable via `StaminaRestore` parameter)
- **Token Validation**: Checks PlayFab inventory for sufficient tokens before restoration
- **Immediate Effect**: Updates `LastStaminaRestore` timestamp to current time

### Implementation Details
- **On-Demand Processing**: Stamina is restored automatically when player interacts with clan system
- **Efficient Queries**: Only processes the current player's stamina, not all players
- **Time-Based Calculation**: Uses `DATEDIFF` to calculate exact restoration amount based on time passed
- **No Background Tasks**: No timer functions or scheduled jobs required
- **Player-Specific**: Each player's stamina is restored independently when they use clan functions

## Clan Attack System

### Attack Mechanics
- **Success Chance**: Based on defending clan's total stamina (all members combined)
- **High Stamina Defense**: Clans with high total stamina are harder to attack successfully
- **Success Rate**: 80% base chance when defender has 0 stamina, decreases to 10% minimum at maximum stamina

### Reputation Rewards
- **Rank-Based**: Rewards based on clan reputation difference (reputation ÷ 1000 = rank)
- **Higher Rank Target**: +5 reputation per rank difference (max 100 total)
- **Same Rank**: 4 reputation
- **Lower Rank**: 3 reputation (minimum)

### Clan Weakening
- **Stamina Reduction**: Successful attacks reduce defender's stamina
- **Reduction Formula**: 5% of total clan stamina (min 10, max 200)
- **Distributed Loss**: Stamina reduction spread across all clan members
- **Defense Weakening**: Lower stamina makes future attacks more likely to succeed

### Attack Formulas & Configuration
```
Success Chance = 80% - (70% × stamina_ratio)
Reputation Reward = 3 + max(0, rank_difference × 5)
Stamina Reduction = 50 per attack (fixed)
Total Attacks to Weaken = 25 members × 4 attacks = 100 attacks
Max Clan Stamina = 50 members × 100 stamina = 5000 stamina
```

### Global Configuration Values
- **Max Clan Members**: 50
- **Max Attacking Members**: 25  
- **Attacks Needed Per Member**: 4
- **Stamina Reduction Per Attack**: 50
- **Max Stamina Per Player**: 100
- **Base Attack Success**: 80%
- **Min Attack Success**: 10%
- **Reputation Per Rank**: 5
- **Manual Restore Cost**: 20 tokens
- **Auto Restore Interval**: 30 minutes 