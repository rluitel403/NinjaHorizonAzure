# Stamina and Reputation Mechanics

## Stamina System

The stamina system controls a player's ability to perform actions, specifically attacking other clans. It features dynamic regeneration and limits based on clan buildings.

### 1. Stamina Regeneration
Stamina regenerates continuously over time, but it is calculated **on-demand** (lazily). There is no background timer updating the database every minute. Instead, the system calculates your current stamina whenever it is needed.

*   **Regeneration Rate:** 1 Stamina per minute (1/60 per second).
*   **When is it updated?**
    1.  **When you check your status:** The server calculates how much you regenerated since you last checked.
    2.  **When you attack:** The server calculates your current stamina before deducting the attack cost.
    3.  **When you are attacked:** The server calculates your current stamina before deducting the damage.
*   **Formula:**
    ```
    CurrentStamina = Min(EffectiveMaxStamina, StoredStamina + (SecondsElapsed * RegenRate))
    ```
    *   `StoredStamina`: The stamina value saved in the database at the last update.
    *   `SecondsElapsed`: Time in seconds since the last update.

### 2. Max Stamina Calculation
A player's maximum stamina is determined by their base stats plus bonuses from their clan's **Bath House**.

*   **Base Max Stamina:** 100
*   **Building Bonus:** Each level of the Bath House adds 20 max stamina.
    *   `Bonus = (BathHouseLevel - 1) * 20`
*   **Effective Max Stamina:**
    ```
    EffectiveMax = BaseMaxStamina + BuildingBonus
    ```
    *   *Example:* A level 3 Bath House gives `(3-1) * 20 = 40` bonus stamina. Total Max = 140.

### 3. Stamina Consumption (Attacking)
*   **Cost:** Attacking another clan costs **10 Stamina**.
*   **Requirement:** Players must have at least 10 stamina to initiate an attack.

### 4. Stamina Loss (Defending)
When a clan is attacked, its members lose stamina. The amount lost and the number of members affected depend on the attacker's buildings.

#### Tea House (Damage per Hit)
Determines how much stamina each affected player loses.
*   **Base Reduction:** 5 Stamina per hit.
*   **Tea House Bonus:** +3 Stamina reduction per level above 1.
*   **Formula:**
    ```
    StaminaLoss = 5 + (AttackerTeaHouseLevel - 1) * 3
    ```

#### Training Centre (Number of Targets)
Determines how many players in the target clan are hit by the attack.
*   **Base Targets:** 1 Player.
*   **Training Centre Bonus:** +1 Target per level above 1.
*   **Formula:**
    ```
    PlayersHit = 1 + (AttackerTrainingCentreLevel - 1) * 1
    ```
    *   *Example:* A level 3 Training Centre hits `1 + (2 * 1) = 3` random players.

### 5. Bleeding State
A clan is considered "Bleeding" when its total collective stamina drops below a critical threshold.

*   **Bleeding Threshold:** 30% of Max Possible Stamina.
*   **Calculation:**
    ```
    BleedingPercentage = TotalClanStamina / TotalMaxPossibleStamina
    IsBleeding = BleedingPercentage <= 0.30
    ```

---

## Reputation System

Reputation is the primary metric for clan ranking. It can be gained through individual contributions or by attacking other clans.

### 1. Contributing Reputation
Players can transfer their personal reputation (earned through gameplay) to their clan.

*   **Mechanism:** Direct transfer from Player Reputation -> Clan Total Reputation.
*   **Constraint:** Players cannot contribute more than they currently possess.

### 2. Gaining Reputation via Attacks
Clans earn reputation by successfully attacking other clans, but **only if the target clan is Bleeding**.

*   **Condition:** Target clan must be in a "Bleeding" state (<= 30% stamina).
*   **Reward Calculation:**
    The reward depends on the difference in reputation between the attacker and the target.

    1.  **Base Reward:** If Target Rank <= Attacker Rank (Target has less or equal rep):
        *   **Reward:** 3 Reputation.

    2.  **Scaled Reward:** If Target Rank > Attacker Rank (Target has more rep):
        *   **Formula:**
            ```
            Reward = 5 + (ReputationDifference * 0.01)
            ```
        *   **Limits:** Minimum 5, Maximum 50.
        *   *Example:* Attacking a clan with 1000 more reputation yields `5 + (1000 * 0.01) = 15` reputation.

### Summary of Constants

| Constant | Value | Description |
| :--- | :--- | :--- |
| `STAMINA_REGEN_PER_SECOND` | 1/60 | ~1 Stamina per minute |
| `BATHHOUSE_STAMINA_PER_LEVEL` | 20 | Max stamina bonus per level |
| `ATTACK_STAMINA_COST` | 10 | Cost to attack |
| `BLEEDING_THRESHOLD` | 0.3 | 30% stamina triggers bleeding |
| `TEAHOUSE_BASE_STAMINA_REDUCTION` | 5 | Base damage to defender stamina |
| `TEAHOUSE_STAMINA_REDUCTION_PER_LEVEL` | 3 | Bonus damage per Tea House level |
| `BASE_REPUTATION_REWARD` | 3 | Reward for attacking lower/equal rank |
| `HIGH_RANK_REPUTATION_REWARD` | 5 | Base reward for attacking higher rank |
| `REPUTATION_SCALING_FACTOR` | 0.01 | 1% of rep diff as bonus reward |

## Technical Implementation Notes

### 1. Lazy Evaluation & Fractional Loss
Since stamina is stored as an integer in the database, but regeneration is continuous:
*   **Fractional Loss:** When an attack occurs, the system calculates the exact stamina (e.g., 45.8), applies the damage, and saves the result as an integer. The fractional part (0.8) is effectively discarded.
*   **Impact:** Frequent updates (being attacked often) can slightly retard the regeneration rate by constantly resetting the fractional progress to zero.

### 2. Concurrency (Race Conditions)
The system uses a "Last Write Wins" approach for updating player statistics.
*   **Scenario:** If a player is spending stamina (attacking) at the exact same moment they are being attacked.
*   **Result:** There is a small risk that one of the updates will overwrite the other. For example, if the damage update finishes last, the player's own stamina expenditure might be reverted (effectively giving them a free attack). This is generally acceptable for this type of game mechanics.
