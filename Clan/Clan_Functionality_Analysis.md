# Clan Functionality Analysis

## Overview
The `ClanFunction` class serves as the entry point for all clan-related operations in the Ninja Horizon Azure backend. It is implemented as an Azure Function that acts as a dispatcher, routing incoming HTTP POST requests to specific handler methods based on an `action` parameter.

## Architecture
-   **Type**: Azure Function (HTTP Trigger).
-   **Trigger**: `POST` requests.
-   **Authorization Level**: `Function` (requires a function key).
-   **Pattern**: Command/Action Dispatcher. The `Run` method reads the `action` field from the request body and delegates execution to a specific private method.
-   **Dependencies**:
    -   `PlayFabUtil`: Handles PlayFab context and authentication.
    -   `ClanService`: Encapsulates the core business logic for clan operations.

## Detailed Functionality
The function supports the following actions:

| Action | Description | Required Parameters |
| :--- | :--- | :--- |
| `createclan` | Creates a new clan. | `ClanName` |
| `applytoclan` | Sends an application to join a specific clan. | `ClanId` |
| `inviteplayer` | Invites a player to join the caller's clan. | `ClanId`, `TargetPlayerId` |
| `acceptapplication` | Accepts a player's application to join the clan. | `ClanId`, `ApplicantEntityId` |
| `rejectapplication` | Rejects a player's application to join the clan. | `ClanId`, `ApplicantEntityId` |
| `getpendingapplications` | Retrieves a list of pending applications for the clan. | `ClanId` |
| `getclandetails` | Retrieves detailed information about a specific clan. | `ClanId` |
| `getclanleaderboard` | Retrieves the global clan leaderboard. | `Count` (optional, default 100) |
| `attackclan` | Initiates an attack on a target clan. | `TargetClanId` |
| `upgradebuilding` | Upgrades a specific building within the clan. | `ClanId`, `BuildingType` |
| `contributereputation` | Contributes player reputation to the clan. | `ClanId`, `Amount` |
| `getmystamina` | Calculates the player's current stamina, accounting for clan bonuses (e.g., Bath House). | None (infers from context) |
| `leaveclan` | Removes the current player from their clan. | None (infers from context) |

## Code Structure
-   **`Run` Method**: The main entry point. It:
    1.  Initializes `PlayFabUtil` and `ClanService`.
    2.  Parses the `action` from the request.
    3.  Logs the action.
    4.  Switches on the `action` string to call the appropriate handler.
    5.  Catches and logs any exceptions, returning a `BadRequestObjectResult`.

-   **Handler Methods**: Private static methods (e.g., `HandleCreateClan`, `HandleApplyToClan`) that:
    1.  Deserialize the request arguments into a strongly-typed object (e.g., `CreateClanRequest`).
    2.  Validate required parameters.
    3.  Call the corresponding method on `ClanService`.
    4.  Return the result (often as a dynamic object or anonymous type).

## Potential Issues and Observations

### 1. Inconsistent Logic Placement
-   **Observation**: Most handlers simply delegate to `ClanService`. However, `HandleGetMyStamina` contains significant business logic (fetching clan membership, checking Bath House level, calculating stamina). `HandleLeaveClan` also directly uses `PlayFabUtil` to remove members instead of going through `ClanService`.
-   **Impact**: This violates the Separation of Concerns principle. Business logic in the Function layer makes it harder to test and reuse.
-   **Recommendation**: Move the stamina calculation logic and the "leave clan" logic into `ClanService`.

### 2. Type Safety
-   **Observation**: The function returns `Task<dynamic>`. While flexible, this hides the response structure from the client and compiler.
-   **Impact**: Clients consuming this API must rely on external documentation or trial-and-error to know the response format.
-   **Recommendation**: Define standard response DTOs (Data Transfer Objects) for each action to improve type safety and documentation.

### 3. Error Handling Granularity
-   **Observation**: The main `try-catch` block catches `Exception` and returns a generic 400 Bad Request with the exception message.
-   **Impact**: Internal server errors (unexpected bugs) are indistinguishable from validation errors (user mistakes) to the client. It may also leak internal error details.
-   **Recommendation**: Catch specific exceptions (e.g., `ArgumentException`, `InvalidOperationException`) for 400 responses and handle generic `Exception` as 500 Internal Server Error, potentially masking the internal message in production.

### 4. Missing Functionality
-   **Observation**: There is a `leaveclan` action, but no `kickmember` action is exposed in the switch statement, even if `ClanService` might support it.
-   **Impact**: Clan leaders may not be able to remove members via this API.

### 5. Input Validation
-   **Observation**: Validation is currently limited to checking for null/empty strings on required fields.
-   **Impact**: Invalid data types or logical constraints (e.g., negative numbers for IDs, though checked for `Amount`) might pass through to the service layer.

### 6. Dependency on "Magic Strings"
-   **Observation**: The routing relies on string matching ("createclan", "applytoclan").
-   **Impact**: Typos in the client request will result in an "Unknown action" error.
-   **Recommendation**: Ensure client SDKs or documentation strictly match these action names.
