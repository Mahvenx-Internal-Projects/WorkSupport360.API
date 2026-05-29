# WorkSupport360 — .NET 8 Backend Documentation

## Table of Contents
1. [Project Setup](#1-project-setup)
2. [Google Sign-In Setup](#2-google-sign-in-setup)
3. [Running in VS Code with Swagger](#3-running-in-vs-code-with-swagger)
4. [Database & EF Core Migrations](#4-database--ef-core-migrations)
5. [API Reference](#5-api-reference)
6. [Testing with REST Client (.http file)](#6-testing-with-rest-client)
7. [Authentication Flow](#7-authentication-flow)
8. [Architecture & Project Structure](#8-architecture--project-structure)
9. [Seed Accounts](#9-seed-accounts)
10. [Business Rules](#10-business-rules)

---

## 1. Project Setup

### Prerequisites
| Tool | Version | Download |
|------|---------|----------|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com/download/dotnet/8.0 |
| MySQL | 8.0+ | https://dev.mysql.com/downloads/mysql/ |
| VS Code | Latest | https://code.visualstudio.com/ |
| EF Core CLI | Latest | `dotnet tool install --global dotnet-ef` |

### VS Code Extensions (required)
- **C# Dev Kit** — `ms-dotnettools.csdevkit` — IntelliSense + debugger
- **.NET Install Tool** — `ms-dotnettools.vscode-dotnet-runtime`
- **REST Client** — `humao.rest-client` — run .http test files

### Step 1 — Create MySQL database
```sql
CREATE DATABASE worksupport360
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;
```

### Step 2 — Update appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=worksupport360;User=root;Password=YOUR_PASSWORD;"
  },
  "Jwt": {
    "Secret": "RANDOM_64_CHAR_STRING_HERE",
    "Issuer": "WorkSupport360",
    "Audience": "WorkSupport360"
  },
  "Google": {
    "ClientId": "YOUR_CLIENT_ID.apps.googleusercontent.com",
    "ClientSecret": "YOUR_CLIENT_SECRET"
  }
}
```

### Step 3 — Restore, migrate, run
```bash
cd backend/
dotnet restore
dotnet ef migrations add InitialCreate
dotnet ef database update
dotnet run
```

The API starts at **http://localhost:5000** and Swagger opens at **http://localhost:5000/swagger**.

---

## 2. Google Sign-In Setup

### Create OAuth credentials in Google Cloud Console

1. Go to https://console.cloud.google.com
2. Create a new project (or use existing): **WorkSupport360**
3. Navigate to **APIs & Services → Credentials**
4. Click **Create Credentials → OAuth Client ID**
5. Application type: **Web application**
6. Name: `WorkSupport360`
7. Add authorized JavaScript origins:
   - `http://localhost:3000` (React dev server)
   - `https://worksupport360.com` (production)
8. Add authorized redirect URIs:
   - `http://localhost:3000` (for GIS popup flow)
   - `https://worksupport360.com`
9. Click **Create** → copy **Client ID** and **Client Secret**

### Add credentials to appsettings.json
```json
"Google": {
  "ClientId": "123456789-abcdef.apps.googleusercontent.com",
  "ClientSecret": "GOCSPX-your-secret-here"
}
```

> **Security note:** Never commit real credentials to Git.
> Use `dotnet user-secrets set "Google:ClientId" "your-id"` for local dev.

### How it works (flow)
```
React frontend
  → User clicks "Sign in with Google"
  → Google shows consent screen
  → Google returns credential (ID token) to frontend
  → Frontend POSTs { idToken: "..." } to POST /api/auth/google
  → Backend validates token with Google library
  → Backend creates/finds user → returns JWT + refresh token
  → Frontend stores tokens, redirects to dashboard
```

### React frontend integration
```typescript
// In your LoginPage.tsx — already wired up in the React project
// Add Google Client ID to your .env file:
// REACT_APP_GOOGLE_CLIENT_ID=123456789-abc.apps.googleusercontent.com

// The Google button calls:
const response = await axios.post('/api/auth/google', {
  idToken: credential   // credential = the token from Google GIS
});
// response.data = { accessToken, refreshToken, role, name, userId, picture }
```

---

## 3. Running in VS Code with Swagger

### Option A — Terminal (simplest)
```bash
cd backend/
dotnet run
# Open http://localhost:5000/swagger
```

### Option B — F5 Debugger
1. Open `backend/` folder in VS Code (not the root!)
2. The `.vscode/launch.json` is already configured
3. Press **F5** → API starts + Swagger opens automatically

### Using Swagger UI
1. Open `http://localhost:5000/swagger`
2. Click **POST /api/auth/login** → **Try it out**
3. Enter credentials:
   ```json
   { "email": "admin@worksupport360.com", "password": "Admin@123!" }
   ```
4. Click **Execute** → copy the `accessToken` from the response
5. Click **Authorize** button (top right 🔒)
6. Enter: `Bearer eyJhbGci...` (paste your token after "Bearer ")
7. Click **Authorize** → all protected endpoints are now unlocked

### Setting breakpoints
- Click the gutter (left of line number) in any `.cs` file to set a breakpoint
- When Swagger hits that endpoint, VS Code pauses execution there
- Hover any variable to inspect its value
- Use **F10** (step over), **F11** (step into), **F5** (continue)

---

## 4. Database & EF Core Migrations

### Migration commands
```bash
# Create initial migration (first time)
dotnet ef migrations add InitialCreate

# Apply pending migrations to database
dotnet ef database update

# Add a new migration after changing Models.cs
dotnet ef migrations add AddReviewsTable

# List all migrations
dotnet ef migrations list

# Roll back to a specific migration
dotnet ef database update PreviousMigrationName

# Remove the last migration (if not applied yet)
dotnet ef migrations remove

# Drop entire database (dev only!)
dotnet ef database drop
```

### When to add a migration
Any time you change `Models/Models.cs` — add a property, change a type, add an index — run:
```bash
dotnet ef migrations add DescribeYourChange
dotnet ef database update
```

---

## 5. API Reference

### Base URL
- **Development:** `http://localhost:5000`
- **Swagger UI:** `http://localhost:5000/swagger`

### Response format
All responses use JSON. Error responses follow this shape:
```json
{
  "message": "Human-readable error description",
  "errorType": "ExceptionClassName",
  "timestamp": "2025-07-15T10:30:00Z"
}
```

### HTTP status codes used
| Code | Meaning |
|------|---------|
| 200 | Success |
| 201 | Created |
| 204 | No content (DELETE) |
| 400 | Bad request / validation error |
| 401 | Unauthenticated (missing/invalid token) |
| 403 | Forbidden (wrong role) |
| 404 | Not found |
| 409 | Conflict (duplicate email, etc.) |
| 500 | Server error |

---

### AUTH ENDPOINTS

#### POST /api/auth/login
Login with email and password.
```json
// Request
{ "email": "admin@worksupport360.com", "password": "Admin@123!" }

// Response 200
{
  "accessToken": "eyJhbGci...",
  "refreshToken": "base64string",
  "role": "admin",
  "name": "Admin User",
  "userId": "guid",
  "picture": null,
  "isNewUser": false
}
```

#### POST /api/auth/google
Sign in or register with a Google ID token.
```json
// Request — send the 'credential' from Google Identity Services
{ "idToken": "eyJhbGci..." }

// Response 200 — same shape as login, isNewUser=true if auto-registered
```

#### POST /api/auth/register
Create a new account.
```json
// Request
{
  "email": "user@company.com",
  "password": "SecurePass@123",
  "name": "Full Name",
  "role": "client",           // admin | freelancer | client
  "companyName": "My Company" // for clients
}
```

#### POST /api/auth/refresh
Exchange refresh token for new access token.
```json
// Request
{ "refreshToken": "base64string" }
// Returns same AuthResponse shape
```

#### POST /api/auth/logout  *(requires auth)*
Revoke current device's refresh token.

#### POST /api/auth/logout-all  *(requires auth)*
Revoke all refresh tokens (logout all devices).

#### GET /api/auth/me  *(requires auth)*
Returns current user info from JWT.

---

### FREELANCER ENDPOINTS

#### GET /api/freelancers
Search freelancers (public — no auth needed).
```
Query params:
  keyword     string   Search alias, role, bio
  skill       string   Filter by skill name
  isAvailable bool     true/false
  maxRate     decimal  Max hourly rate
  currency    string   USD | INR | EUR
  country     string   e.g. "India"
  minTrustScore int    e.g. 80
  page        int      Default 1
  pageSize    int      Default 20, max 50
```

#### GET /api/freelancers/{id}
Get public profile + reviews. Increments profileViews.

#### GET /api/freelancers/me  *(freelancer)*
Get own private data (real name, company, earnings).

#### GET /api/freelancers/me/stats  *(freelancer)*
Get earnings dashboard stats.

#### PUT /api/freelancers/me  *(freelancer)*
Update profile, skills, availability.

#### PATCH /api/freelancers/me/availability  *(freelancer)*
Toggle availability: send `true` or `false` in body.

---

### REQUESTS ENDPOINTS

#### GET /api/requests  *(auth)*
List requests. Admins see all; clients/freelancers see their own.
```
Query: status=pending|scheduled|approved|rejected|completed
```

#### POST /api/requests  *(client)*
Submit a demo/interview/quick-support request.

#### GET /api/requests/{id}  *(auth)*
Get request details.

#### PATCH /api/requests/{id}/status  *(admin)*
Update status and add admin notes.

---

### MEETINGS ENDPOINTS

#### GET /api/meetings  *(auth)*
List meetings filtered by caller's role.

#### POST /api/meetings  *(admin)*
Schedule a meeting from a request. **Sends email invites automatically.**

#### PATCH /api/meetings/{id}/outcome  *(admin)*
Record meeting result: `approved | rejected | pending_decision`.

#### PATCH /api/meetings/{id}/cancel  *(admin)*
Cancel a meeting.

---

### PROJECTS ENDPOINTS

#### GET /api/projects  *(auth)*
List projects (role-filtered). Optional `?status=active`.

#### GET /api/projects/{id}  *(auth)*
Get project details including milestones.

#### POST /api/projects  *(admin)*
Create project with skills and milestones. **Notifies freelancer automatically.**

#### PATCH /api/projects/{id}/progress  *(admin, freelancer)*
Update progress percentage and/or status.

#### PATCH /api/projects/{projectId}/milestones/{milestoneId}  *(auth)*
Update milestone status: `pending | in_progress | submitted | approved | paid`.

---

### TIMESHEETS ENDPOINTS

#### GET /api/timesheets  *(auth)*
List timesheets (role-filtered). Optional `?status=submitted`.

#### POST /api/timesheets  *(freelancer)*
Submit weekly timesheet with daily entries.
**Auto-generates invoice when approved.**

#### PATCH /api/timesheets/{id}/approve  *(admin, client)*
Approve or reject with optional reason.
- **Approve:** Creates invoice automatically, sends email to freelancer.
- **Reject:** Sends rejection email with reason.

---

### INVOICES ENDPOINTS

#### GET /api/invoices  *(auth)*
List invoices (role-filtered). Optional `?status=pending`.

#### GET /api/invoices/{id}  *(auth)*
Get invoice with line items.

#### PATCH /api/invoices/{id}/mark-paid  *(admin)*
Mark paid, create payment record, update freelancer earnings, send email.

#### POST /api/invoices/mark-overdue  *(admin)*
Mark all past-due unpaid invoices as overdue.

---

### PAYMENTS ENDPOINTS

#### GET /api/payments  *(auth)*
List payments (role-filtered).

---

### NOTIFICATIONS ENDPOINTS

#### GET /api/notifications  *(auth)*
Get last 50 notifications. Optional `?unreadOnly=true`.

#### PATCH /api/notifications/{id}/read  *(auth)*
Mark one notification as read.

#### POST /api/notifications/mark-all-read  *(auth)*
Mark all as read.

#### DELETE /api/notifications/{id}  *(auth)*
Delete a notification.

---

### STANDUPS ENDPOINTS

#### POST /api/standups  *(freelancer)*
Submit daily standup.

#### GET /api/standups/project/{projectId}  *(auth)*
List standups for a project, latest first.

---

### REVIEWS ENDPOINTS

#### POST /api/reviews  *(client)*
Submit a 1-5 star review for a freelancer. Auto-recalculates their average rating.

---

### ADMIN ENDPOINTS

#### GET /api/admin/stats  *(admin)*
Platform dashboard stats: revenue, projects, requests, commission, growth.

#### GET /api/admin/leaderboard  *(admin)*
Top 10 earning freelancers for the current month.

#### GET /api/admin/reports/revenue  *(admin)*
Revenue by month for last 12 months.

#### PATCH /api/admin/users/{userId}/role  *(admin)*
Promote/change user role. Body: `"freelancer"` (plain JSON string).

---

## 6. Testing with REST Client

### Setup
1. Install the **REST Client** extension in VS Code
2. Open `WorkSupport360.http`
3. Click **Send Request** above any `###` block

### Quick start flow
```
1. ### 1. Login as admin  → copy accessToken
2. Set @adminToken = <pasted token>
3. ### 46. Platform stats  → verify admin access
4. ### 10. Search freelancers  → get a freelancer GUID
5. ### 20. Create request  → use freelancer GUID
6. ### 23. Schedule meeting  → use request GUID
7. ### 27. Create project  → use client + freelancer GUIDs
8. ### 30. Submit timesheet  → use project GUID
9. ### 31. Approve timesheet  → invoice auto-generated
10. ### 35. Mark invoice paid  → earnings updated
```

---

## 7. Authentication Flow

### Email/password flow
```
POST /api/auth/register  →  201 Created + tokens
POST /api/auth/login     →  200 OK + { accessToken, refreshToken }
Use accessToken in header: Authorization: Bearer <token>
On 401 → POST /api/auth/refresh with refreshToken → new tokens
On logout → POST /api/auth/logout with refreshToken
```

### Google Sign-In flow
```
Frontend: Google GIS button click
  → google.accounts.id.initialize({ client_id: "..." })
  → User selects Google account
  → Callback receives { credential: "eyJhbGci..." }  ← this is the ID token

Frontend: POST /api/auth/google { idToken: credential }

Backend:
  1. GoogleJsonWebSignature.ValidateAsync(idToken)  ← verifies with Google
  2. Extracts: email, name, picture, sub (Google ID)
  3. Finds existing user OR creates new client account
  4. Returns JWT + refresh token

Frontend: stores tokens, redirects
```

### Token expiry
| Token | Expiry | Action on expiry |
|-------|--------|-----------------|
| Access token | 60 minutes | Use refresh token to get new one |
| Refresh token | 30 days | User must log in again |

### Role-based access
| Endpoint category | admin | freelancer | client |
|---|---|---|---|
| Search freelancers | ✅ | ✅ | ✅ |
| Create requests | — | — | ✅ |
| Schedule meetings | ✅ | — | — |
| Create projects | ✅ | — | — |
| Submit timesheets | — | ✅ | — |
| Approve timesheets | ✅ | — | ✅ |
| Mark invoices paid | ✅ | — | — |
| Admin stats/reports | ✅ | — | — |

---

## 8. Architecture & Project Structure

```
backend/
├── WorkSupport360.API.csproj   ← NuGet packages
├── Program.cs                  ← App startup, DI, CORS, Swagger, seed
├── appsettings.json            ← Config (DB, JWT, Google, SMTP)
├── appsettings.Development.json ← Dev-only logging
├── WorkSupport360.http         ← REST Client test file (49 requests)
│
├── Models/
│   └── Models.cs               ← All 20 EF Core entities
│
├── Data/
│   └── AppDbContext.cs         ← DbContext with full relationship config
│
├── DTOs/
│   └── DTOs.cs                 ← All request/response record types
│
├── Services/
│   ├── AuthService.cs          ← JWT, Google OAuth, refresh token logic
│   └── EmailService.cs         ← SMTP via MailKit, HTML templates
│
├── Controllers/
│   ├── AuthController.cs       ← /api/auth/*
│   ├── FreelancersController.cs ← /api/freelancers/*
│   ├── RequestsController.cs   ← /api/requests/*
│   ├── MeetingsController.cs   ← /api/meetings/*
│   ├── ProjectsController.cs   ← /api/projects/*
│   ├── TimesheetsController.cs ← /api/timesheets/*
│   ├── InvoicesController.cs   ← /api/invoices/*
│   ├── PaymentsController.cs   ← /api/payments/*
│   ├── NotificationsController.cs ← /api/notifications/*
│   ├── StandupsController.cs   ← /api/standups/*
│   ├── ReviewsController.cs    ← /api/reviews/*
│   └── AdminController.cs      ← /api/admin/*
│
├── Middleware/
│   └── ErrorHandlingMiddleware.cs ← Global exception → JSON error
│
├── Extensions/
│   └── ClaimsPrincipalExtensions.cs ← GetUserId(), GetRole() helpers
│
└── .vscode/
    ├── launch.json             ← F5 debug config (auto-opens Swagger)
    ├── tasks.json              ← Build task
    └── settings.json           ← C# formatter settings
```

### Key packages
| Package | Purpose |
|---------|---------|
| `Pomelo.EntityFrameworkCore.MySql` | MySQL driver for EF Core |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT validation |
| `Google.Apis.Auth` | Google ID token server-side verification |
| `BCrypt.Net-Next` | Password hashing (bcrypt) |
| `Swashbuckle.AspNetCore` | Swagger UI + OpenAPI spec |
| `MailKit` | SMTP email with HTML templates |

---

## 9. Seed Accounts

These are auto-created when you run the app for the first time (on an empty database):

| Role | Email | Password | Notes |
|------|-------|----------|-------|
| Admin | admin@worksupport360.com | Admin@123! | Full platform access |
| Freelancer | rahul@example.com | Test@123! | Rahul S. — React/AWS expert |
| Freelancer | priya@example.com | Test@123! | Priya K. — ML/Python |
| Client | john@abccorp.com | Test@123! | ABC Corp — Starter plan |
| Client | sarah@xyzltd.com | Test@123! | XYZ Ltd — Growth plan |

One active project is also seeded: "Fintech Dashboard — Phase 1" (ABC Corp + Rahul S., 42% progress).

---

## 10. Business Rules

### Platform commission
| Tier | Commission |
|------|-----------|
| Tier 1 (basic) | 15% |
| Tier 2 (pro) | 15% |
| Tier 3 (expert) | 10% |
| Tier 4 (mentor) | 10% |

Commission is deducted automatically when an invoice is generated.

### Timesheet → Invoice flow
```
Freelancer submits timesheet
  → Admin or client approves
  → Invoice auto-generated (status: pending)
  → Admin marks invoice paid
  → Payment record created
  → Freelancer TotalEarned updated
  → Email sent to freelancer
```

### Alias privacy
- `Freelancer.AliasName` is shown publicly (e.g. "Rahul S.")
- `Freelancer.RealName` and `Freelancer.CurrentCompany` are stored but never returned in public API responses
- `GET /api/freelancers/{id}` returns only alias, not real name or company
- `GET /api/freelancers/me` (authenticated freelancer) returns private data

### Rating recalculation
When a review is submitted via `POST /api/reviews`:
- `Freelancer.ReviewCount` is incremented
- `Freelancer.Rating` is recalculated as the average of all ratings

### Google account merging
If a user registers with email first, then signs in with Google using the same email:
- The Google ID is attached to the existing account
- No duplicate account is created
- Password login still works alongside Google login
