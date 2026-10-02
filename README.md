# Kue Backend API

High-performance RESTful and real-time backend API for Kue, an all-in-one entertainment tracking and social platform for movies, TV series, anime, manga, and video games.

Built with .NET 10, C# 13, PostgreSQL, Entity Framework Core, and ASP.NET Core SignalR.

---

## Key Features

### Multi-Source Media Catalog

- Search, discovery, and metadata synchronization across five entertainment formats:
  - Movies and TV Series: Powered by TMDB API
  - Anime and Manga: Powered by AniList GraphQL API (no API key required)
  - Video Games: Powered by IGDB API via Twitch OAuth credentials
- In-memory caching for external API responses to optimize latency and prevent upstream rate limits.

### Personal Library and Ratings

- Track media progression states: `watching`, `reading`, `playing`, `completed`, `on_hold`, `dropped`, `planned`.
- Decimal score system from 1.0 to 10.0 with automatic media score averages and distribution statistics.
- Personal favorites and entry tracking metadata.

### Custom Curated Lists

- Create ranked or themed lists with titles, descriptions, and custom ordering.
- Granular visibility control: Public (discoverable by all users) or Private (creator only).

### Social and Privacy Engine

- Mutual friendship system: send, accept, reject, cancel, and remove friend requests.
- Steam-style profile privacy:
  - Profiles can be marked as Private (`isPrivate = true`).
  - Private profiles restrict detailed library entries, ratings, and stats strictly to mutual friends.
  - Public media pages automatically omit private accounts from rating lists to prevent privacy leaks.

### Real-Time Push Notifications

- Persistent WebSocket connection via SignalR (`/hubs/notifications`).
- Push notifications for incoming friend requests, accepted friendships, and system events.
- Unread badge counters and read-status management endpoints.

### Defensive Security Hardening

- Authentication: Short-lived JWT access tokens with secure HttpOnly refresh token cookies.
- Token rotation and RFC 6749 reuse detection (automatic session revocation on replay attacks).
- Brute-force protection: IP-based Token Bucket rate limiting on authentication routes.
- Password reset: single-use 256-bit tokens stored only as SHA-256 hashes with a one-hour expiry, delivered by transactional email (Brevo). The token is never part of any API response in any environment — it exists only inside the emailed link, so knowledge of an email address alone cannot grant a password reset.
- Timing-safe password reset: reset mail is dispatched through a background queue so a known address is not measurably slower to respond than an unknown one, and a per-user cooldown suppresses repeated mail to a single account.
- Global exception handling middleware: sanitizes all unhandled server errors into standardized JSON to prevent stack trace or database structure leakage.
- IDOR prevention: strict server-side ownership checks across library, list, rating, and notification actions.
- Administrative controls: self-locking initial bootstrap endpoint (`/admin/bootstrap`), user role management, account suspension with ban reasons, and media catalog synchronization.

---

## Technology Stack

| Component               | Technology                                                |
| ----------------------- | --------------------------------------------------------- |
| Runtime / Framework     | .NET 10 (ASP.NET Core Web API)                            |
| Language                | C# 13                                                     |
| Database                | PostgreSQL                                                |
| ORM                     | Entity Framework Core 10 (Npgsql)                         |
| Real-Time Communication | Microsoft ASP.NET Core SignalR (WebSockets)               |
| Authentication          | JWT + Secure HttpOnly Refresh Cookies                     |
| Documentation           | OpenAPI + Scalar Interactive Reference                    |
| Cryptography            | ASP.NET Identity PasswordHasher (PBKDF2 with HMAC-SHA512) |

---

## Project Structure

```
Kue.Api/
├── Controllers/         # API Controllers (Auth, Media, Library, Ratings, Lists, Users, Friends, Notifications, Admin)
├── Data/                # AppDbContext and Entity configurations
├── Dtos/                # Request and response data transfer objects
├── Entities/            # Database entity models
├── Extensions/          # Service collections and middleware configurations
├── Hubs/                # SignalR WebSockets hubs
├── Middlewares/         # Global exception handling and error sanitization
├── Migrations/          # EF Core schema migrations
├── Services/            # Core business logic (Auth, External APIs, Notifications, Email, Background)
├── Program.cs           # Application entry point and HTTP pipeline
└── appsettings.json     # Configuration file
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL instance (local or Docker)

### 1. Clone and Navigate

```bash
git clone https://github.com/CanDundarCSE/kue-backend.git
cd kue-backend/Kue.Api
```

### 2. Configuration

Configure your database connection, JWT settings, and external API keys in `appsettings.json`, or via .NET user-secrets / environment variables:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=kue_db;Username=postgres;Password=your_db_password"
  },
  "Jwt": {
    "Key": "REPLACE_WITH_YOUR_OWN_SECURE_RANDOM_SECRET_KEY_MIN_32_CHARS",
    "Issuer": "KueApi",
    "Audience": "KueClients",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "Tmdb": {
    "ApiKey": "your_tmdb_api_key",
    "BearerToken": ""
  },
  "Igdb": {
    "ClientId": "your_twitch_client_id",
    "ClientSecret": "your_twitch_client_secret"
  },
  "App": {
    "FrontendUrl": "http://localhost:3000"
  },
  "Brevo": {
    "ApiKey": "your_brevo_api_key",
    "SenderEmail": "no-reply@yourdomain.com",
    "SenderName": "Kue",
    "PasswordResetTemplateId": ""
  }
}
```

> **API Credentials Notes:**
>
> - **AniList:** Public GraphQL endpoint; no API key is required.
> - **TMDB:** Obtain your API Key or Read Access Token from the [TMDB Developer Portal](https://www.themoviedb.org/settings/api).
> - **IGDB:** Obtain your `ClientId` and `ClientSecret` from the [Twitch Developer Console](https://dev.twitch.tv/console). Kue automatically negotiates OAuth App Access Tokens via Client Credentials Flow and caches tokens in memory.
> - **Brevo:** Obtain your API key from the Brevo dashboard under *SMTP & API*. The free plan includes 300 emails per day. `App:FrontendUrl` is the base URL that password reset links are built from, and it must match an origin allowed by the CORS policy. Setting `Brevo:PasswordResetTemplateId` switches the password reset email from the built-in HTML body to a Brevo drag-and-drop template; leave it empty to use the inline body.
>
> **Brevo senders:** A sender must be verified in the Brevo dashboard before the API will accept it. Brevo single-sender verification lets you send to any recipient from a personal address, so no domain purchase is required. Note that a `gmail.com` sender cannot publish SPF or DKIM records, so Gmail, Yahoo, and Microsoft may filter these messages into spam. Authenticating a domain fixes that and is the recommended step before real users depend on password resets.
>
> **Security Notice:** Never commit production JWT secrets or API keys to version control. Generate a cryptographically secure key:
>
> ```bash
> openssl rand -base64 32
> ```
>
> For local development, prefer using .NET Secret Manager:
>
> ```bash
> dotnet user-secrets set "Jwt:Key" "your_generated_secret_key"
> dotnet user-secrets set "Tmdb:ApiKey" "your_tmdb_key"
> dotnet user-secrets set "Igdb:ClientId" "your_client_id"
> dotnet user-secrets set "Igdb:ClientSecret" "your_client_secret"
> dotnet user-secrets set "App:FrontendUrl" "http://localhost:3000"
> dotnet user-secrets set "Brevo:ApiKey" "xkeysib-your_brevo_api_key"
> dotnet user-secrets set "Brevo:SenderEmail" "your_verified_sender@email.com"
> ```

### 3. Run Database Migrations

Ensure the EF Core CLI tool is installed, then apply schema migrations to your database:

```bash
# Install EF Core CLI tool globally (if not already installed)
dotnet tool install --global dotnet-ef

# Apply migrations
dotnet ef database update
```

### 4. Run the Application

```bash
dotnet run
```

The server will start on:

- HTTPS: `https://localhost:7230`
- HTTP: `http://localhost:5148`

### 5. Interactive API Documentation

Open Scalar API documentation in your browser:

- `https://localhost:7230/scalar/v1`
- `http://localhost:5148/scalar/v1`

---

## API Overview

| Route Group   | Base Path                                          | Description                                                                                   |
| ------------- | -------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Auth          | `/api/v1/auth`                                     | Registration, login, token rotation, logout, password resets                                  |
| Media         | `/api/v1/media`                                    | Unified search, discovery, trending, and details for movies, series, anime, manga, and games  |
| Library       | `/api/v1/me/library`                               | Personal library entry statuses, progress updates, and favorites                              |
| Ratings       | `/api/v1/ratings` and `/api/v1/media/{id}/ratings` | Submit decimal ratings, view user ratings, public media scores and distributions              |
| Lists         | `/api/v1/lists`                                    | Custom list creation, item reordering, and visibility management                              |
| Users         | `/api/v1/users`                                    | Public profile retrieval, friend-gated libraries, custom lists, and profile settings          |
| Friends       | `/api/v1/friends`                                  | Friend request workflows, friend lists, and status queries                                    |
| Notifications | `/api/v1/notifications`                            | Notification list, unread counts, and mark-as-read endpoints                                  |
| Real-time Hub | `/hubs/notifications`                              | SignalR WebSocket hub for real-time push events (`ReceiveNotification`, `UnreadCountUpdated`) |
| Admin         | `/api/v1/admin`                                    | Initial admin bootstrap, user management, ban enforcement, and media sync                     |

---
