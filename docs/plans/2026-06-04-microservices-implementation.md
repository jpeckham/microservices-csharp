# Microservices Social App Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a minimal social media microservices solution with a C# Blazor web app, C# Web APIs, MongoDB/Cosmos-compatible persistence, Azure Service Bus-compatible integration events, and Docker Compose local orchestration.

**Architecture:** Five independent API projects own separate Mongo databases. A Blazor Server app composes REST calls for the browser. Events are shared contracts and published through a small abstraction that can use Azure Service Bus in production and a local equivalent for demos.

**Tech Stack:** .NET 8, ASP.NET Core Web API, Blazor Server, MongoDB.Driver, JWT bearer auth, Azure.Messaging.ServiceBus-compatible event contracts, Docker Compose.

---

### Task 1: Scaffold Solution

**Files:**
- Create: `MicroservicesSocial.sln`
- Create: `src/Shared.Contracts/Shared.Contracts.csproj`
- Create: `src/Identity.Api/Identity.Api.csproj`
- Create: `src/Post.Api/Post.Api.csproj`
- Create: `src/Social.Api/Social.Api.csproj`
- Create: `src/Engagement.Api/Engagement.Api.csproj`
- Create: `src/Feed.Api/Feed.Api.csproj`
- Create: `src/Social.Web/Social.Web.csproj`
- Create: `tests/Architecture.Tests/Architecture.Tests.csproj`

**Steps:**
1. Generate projects with `dotnet new`.
2. Add project references to `Shared.Contracts`.
3. Add test project and reference all API projects that expose contract-level types.
4. Run `dotnet build MicroservicesSocial.sln` and fix scaffold compile errors only.

### Task 2: Shared Contracts and Infrastructure Abstractions

**Files:**
- Create: `src/Shared.Contracts/Events/IntegrationEvents.cs`
- Create: `src/Shared.Contracts/Messaging/IEventPublisher.cs`
- Create: `src/Shared.Contracts/Auth/AuthConstants.cs`
- Test: `tests/Architecture.Tests/ContractShapeTests.cs`

**Steps:**
1. Write failing tests proving expected event names and payload identifiers exist.
2. Run `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj` and verify failure.
3. Add event records and publisher abstraction.
4. Rerun tests.

### Task 3: Identity Service

**Files:**
- Create: `src/Identity.Api/Models/UserDocument.cs`
- Create: `src/Identity.Api/Services/UserService.cs`
- Modify: `src/Identity.Api/Program.cs`
- Test: `tests/Architecture.Tests/IdentityEndpointTests.cs`

**Steps:**
1. Write failing minimal API integration tests for register, login, profile by handle, and display-name update.
2. Implement Mongo-backed user storage using `MongoDB.Driver`.
3. Add password hashing with ASP.NET Core `PasswordHasher`.
4. Add JWT creation and validation.
5. Publish `UserCreated` and `UserProfileUpdated` events.

### Task 4: Post Service

**Files:**
- Create: `src/Post.Api/Models/PostDocument.cs`
- Create: `src/Post.Api/Services/PostService.cs`
- Modify: `src/Post.Api/Program.cs`
- Test: `tests/Architecture.Tests/PostEndpointTests.cs`

**Steps:**
1. Write failing tests for create, edit, delete, list by user, and search.
2. Implement direct Mongo CRUD.
3. Publish `PostCreated`, `PostUpdated`, and `PostDeleted`.

### Task 5: Social Service

**Files:**
- Create: `src/Social.Api/Models/FollowDocument.cs`
- Create: `src/Social.Api/Services/SocialGraphService.cs`
- Modify: `src/Social.Api/Program.cs`
- Test: `tests/Architecture.Tests/SocialEndpointTests.cs`

**Steps:**
1. Write failing tests for follow, unfollow, followers, and following.
2. Implement direct Mongo CRUD with unique follower/following relationship enforcement.
3. Publish `UserFollowed` and `UserUnfollowed`.

### Task 6: Engagement Service

**Files:**
- Create: `src/Engagement.Api/Models/LikeDocument.cs`
- Create: `src/Engagement.Api/Models/CommentDocument.cs`
- Create: `src/Engagement.Api/Services/EngagementService.cs`
- Modify: `src/Engagement.Api/Program.cs`
- Test: `tests/Architecture.Tests/EngagementEndpointTests.cs`

**Steps:**
1. Write failing tests for like, unlike, comment, and comment list.
2. Implement direct Mongo CRUD.
3. Publish `LikeAdded`, `LikeRemoved`, and `CommentAdded`.

### Task 7: Feed Service

**Files:**
- Create: `src/Feed.Api/Models/FeedEntryDocument.cs`
- Create: `src/Feed.Api/Services/FeedProjectionService.cs`
- Modify: `src/Feed.Api/Program.cs`
- Test: `tests/Architecture.Tests/FeedProjectionTests.cs`

**Steps:**
1. Write failing tests for projection upsert/delete and timeline ordering.
2. Implement Mongo-backed feed entries.
3. Add simple event-consumer endpoints for local event delivery and background handlers for transport integration.

### Task 8: Blazor Web App

**Files:**
- Modify: `src/Social.Web/Program.cs`
- Create: `src/Social.Web/Services/*.cs`
- Create: `src/Social.Web/Components/Pages/*.razor`
- Create: `src/Social.Web/Components/Layout/MainLayout.razor`
- Create: `src/Social.Web/wwwroot/app.css`

**Steps:**
1. Add pages for login, register, feed, profile, and post detail/comments.
2. Implement service clients for each API.
3. Reuse the reference app's practical layout style while keeping the microservice UI lean.

### Task 9: Docker Compose

**Files:**
- Create: `docker-compose.yml`
- Create: `src/*/Dockerfile`
- Create: `.env.example`

**Steps:**
1. Add MongoDB local container.
2. Add each API service and web app.
3. Configure per-service database names and URLs.
4. Document Azure Cosmos DB and Azure Service Bus environment variables.

### Task 10: Verification

**Commands:**
- `dotnet test MicroservicesSocial.sln`
- `dotnet build MicroservicesSocial.sln`
- `docker compose config`

**Acceptance:**
- Tests and build complete successfully.
- Docker Compose configuration validates.
- Docs explain service decomposition, diagrams, migration, and tradeoffs.
