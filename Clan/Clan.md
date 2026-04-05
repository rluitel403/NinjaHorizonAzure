1. Core Principles

Server is authoritative — all updates to player stats, stamina, reputation, and clan-level data happen server-side.

Player stats stored using Progression / Player Statistics API.

Group statistics store clan-level aggregates (total reputation, gold, points).

Aggregates like total stamina or bleeding percentage are calculated dynamically from player stats on-demand, not stored in the group.

PlayFab Groups API manages memberships, roles, and applications.

Concurrency handled via atomic operations (Progression API for player stats, UpdateGroupStatistics for clan aggregates).

Heavy data fetches (buildings, member stamina, bleeding percentage) are done only when the player interacts with the clan (detail view or attack).

2. Player Statistics (Progression API)
Field	Type	PlayFab API	Purpose
stamina	int	UpdatePlayerStatistics	Last known stamina
max_stamina	int	UpdatePlayerStatistics	Maximum stamina
stamina_last_update	int	UpdatePlayerStatistics	Unix timestamp of last stamina update
reputation	int	UpdatePlayerStatistics	Player’s contribution points

Player stats are independent per player entity.

Use Progression API for atomic updates.

3. Clan / Group Logic
Metric	How to Handle	PlayFab API
Total Reputation	Cached in clan_total_reputation; increment atomically when a member contributes	UpdateGroupStatistics
Clan Gold / Tokens	Cached as group statistic; increment/decrement atomically	UpdateGroupStatistics
Total Stamina	Calculated dynamically from all member stamina and stamina_last_update	GetStatisticsForEntities
Bleeding Percentage	Calculated dynamically from aggregated stamina	Server-side calculation
Clan Membership / Roles	Managed via group roles	GetGroup, InviteToGroup, ApplyToGroup, AcceptGroupApplication, RejectGroupApplication
Pending Applications	Managed natively	GetGroupApplications, ApplyToGroup, Accept/RejectGroupApplication
4. Player Actions

Attack / Spend Stamina:

Server calculates currentStamina = min(max_stamina, stamina + (now - stamina_last_update) * regenRate).

Validate currentStamina >= staminaCost.

Deduct stamina, update stamina and stamina_last_update using Progression API.

Update clan totals or other metrics as needed (e.g., clan_total_reputation).

Contribute Points / Reputation:

Increment reputation via Progression API.

Increment clan total reputation atomically via UpdateGroupStatistics.

5. Clan Actions & Applications
Action	Server Responsibility	PlayFab API
Invite Member	Add a player to the clan	InviteToGroup → AcceptGroupInvitation
Apply to Join	Player requests membership	ApplyToGroup
Approve / Reject Application	Leader validates membership	AcceptGroupApplication / RejectGroupApplication
Upgrade Buildings	Validate leader, update building levels	UpdateGroupData
6. Leaderboards / Top Clans

Metric: clan_total_reputation (group statistic).

Update: atomic increment when member contributes.

Retrieve top 100: GetLeaderboardForStatistic.

Metadata fetch: on-demand, per clan, using GetGroup when player views details or attacks.

7. On-Demand Metadata Fetching

Leaderboard / clan list view:

Fetch only clan ID, name, rank, and clan_total_reputation.

Avoid fetching heavy data like buildings, member stats, or stamina.

Clan detail / attack view:

Fetch GetGroup for clan: buildings, gold, member list.

Fetch GetStatisticsForEntities for all members to calculate:

Current stamina per player

Bleeding percentage (server-side formula)
    - Each attack on clan reduces a random players stamina(come up with formula). 
    - If a clan's total stamina is low enough, the clan is considered bleeding. A bleeding clan will start to give reputation.
    - Note that how much reputation you get depends on the clans ranking. So if clan is below my rank and its bleeding, it only gives 3 reps. But if a clan is above me and bleeding, it could be 5 reps - 50 reps. The amount is based on the rep diff. Make it fair to catch up.

Buildings(Level 1 - 5):
    - Upgrading Bath House increases clan's members max stamina by 20. So Up to 180. Note that clan member can upgrade their max stamina using tokens to increase their max stamina additionally by 100.
    - Upgrading Tea House increases how much a random players stamina reduces when clan members attack. Give a fair value per level.
    - Upgrading Training Centre increases how many random players stamina reduces per attack. Give a fair Value
    - All calculation above must account that a clan can have N number of players, and bleeding a clan should be fair calculation.

Use this data to validate attacks and display accurate clan status.

Benefit: efficient API usage, scalable for large clans, ensures accurate stats at interaction time.

8. Concurrency Handling

Player-specific stats: independent updates per player; no conflicts.

Group statistics: use atomic increments via UpdateGroupStatistics.

Sensitive operations (building upgrades, attacks) validated server-side.

9. PlayFab API References (Server-Side)
API	Purpose
UpdatePlayerStatistics	Update player-specific stats (Progression API: stamina, reputation)
GetStatisticsForEntities	Fetch multiple players’ stats for dynamic aggregation
UpdateGroupStatistics	Update numeric group stats (total reputation, gold) atomically
GetGroupStatistics	Fetch current group stats
GetLeaderboardForStatistic	Fetch top clans by numeric group stat
GetGroup	Fetch clan metadata (buildings, gold, member list) on-demand
ApplyToGroup	Player applies to join a clan
GetGroupApplications	Leader fetches pending applications
AcceptGroupApplication / RejectGroupApplication	Leader approves or rejects membership
InviteToGroup / AcceptGroupInvitation	Leader invites a player; player accepts

10. All API calls must be efficent. We should minimize the number of calls to get relevant data. Try to use best practice of playfab and check if any new api's exist that can make work efficent.