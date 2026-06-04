# Goal

Analyze the existing social media application located in:

```text
../domain-driven-design-csharp
```

and create a new implementation that delivers approximately the same end-user behavior and user experience while using a strict, minimal microservices architecture.

The objective is to compare architectural styles, not to reproduce Domain-Driven Design patterns.

## Success Criteria

A user should be able to:

* Register an account
* Log in
* View profiles
* Create posts
* Edit posts
* Delete posts
* Follow users
* Unfollow users
* Like posts
* Comment on posts
* View a personalized timeline/feed
* View another user's profile and posts

The user experience should remain as close as practical to the existing application.

## Architectural Constraints

### Do Not Use

Avoid implementing:

* Domain-Driven Design
* Aggregates
* Value Objects
* Domain Services
* Domain Events inside the application layer
* Repository pattern abstractions
* CQRS unless absolutely required
* Event Sourcing
* Complex enterprise patterns

This project is intended to demonstrate microservices, not DDD.

### Use

Prefer:

* Simple CRUD services
* REST APIs
* Direct Entity Framework DbContext usage
* Simple service classes
* Message-based integration only where service boundaries require it
* Clear ownership of data by service

## Service Boundaries

Create the following services:

### Identity Service

Responsibilities:

* Registration
* Authentication
* User profiles

Owns:

* Users
* Profile information

Database:

```text
IdentityDb
```

### Post Service

Responsibilities:

* Create posts
* Edit posts
* Delete posts
* Retrieve posts

Owns:

```text
Posts
```

Database:

```text
PostDb
```

### Social Service

Responsibilities:

* Follow
* Unfollow
* Social graph queries

Owns:

```text
FollowRelationships
```

Database:

```text
SocialDb
```

### Engagement Service

Responsibilities:

* Likes
* Comments

Owns:

```text
Likes
Comments
```

Database:

```text
EngagementDb
```

### Feed Service

Responsibilities:

* Build timeline/feed projections
* Serve timeline queries

Owns:

```text
FeedEntries
```

Database:

```text
FeedDb
```

Feed Service is read-model oriented and receives updates through integration events.

## Integration Rules

### Synchronous Communication

Use REST APIs for commands initiated by users.

Examples:

```text
Browser -> Post Service
Browser -> Identity Service
Browser -> Social Service
```

### Asynchronous Communication

Use a message broker for cross-service updates.

Examples:

```text
UserCreated
PostCreated
PostDeleted
UserFollowed
UserUnfollowed
CommentAdded
LikeAdded
```

Services should react to events and maintain their own local data as needed.

## Data Ownership Rules

Strictly enforce:

```text
One Service = One Database
```

No service may:

* Read another service's database
* Join across databases
* Share tables

All cross-service data must arrive through APIs or events.

## Solution Structure

Create a separate deployable project for each service.

Example:

```text
src/

  Identity.Api
  Post.Api
  Social.Api
  Engagement.Api
  Feed.Api

  Shared.Contracts
```

## Infrastructure

Use:

* .NET 8
* ASP.NET Core Web APIs
* Entity Framework Core
* SQL Server or SQLite
* Docker Compose
* RabbitMQ (or equivalent lightweight broker)

Keep infrastructure minimal and understandable.

## Deliverables

1. Analyze the existing solution and identify user-facing functionality.
2. Produce a service decomposition document.
3. Produce architecture diagrams.
4. Produce a migration strategy from the monolith to microservices.
5. Implement the new microservices solution.
6. Create Docker Compose orchestration.
7. Demonstrate end-to-end functionality.
8. Document tradeoffs between:

   * Existing DDD monolith
   * New microservices implementation

The final solution should be intentionally simple, educational, and focused on illustrating microservice architecture rather than advanced domain modeling.
