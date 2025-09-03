# MDWAPI (Starter)

ASP.NET Core 8 Web API for integrating external providers (e.g., Shopee) with **DB-backed random auth tokens**.

## Features
- SQL Server + EF Core
- Custom Authentication (Bearer **random token** stored in `UserTokens` with expiry)
- Auth endpoints: `/api/Auth/login`, `/api/Auth/revoke`, `/api/Auth/me`
- Health check: `/health`
- Swagger with Bearer token support
- Shopee integration scaffold via named `HttpClient`

## Quickstart

1. **Dependencies (NuGet):**
   - Microsoft.EntityFrameworkCore.SqlServer
   - Microsoft.EntityFrameworkCore.Design
   - Swashbuckle.AspNetCore
   - BCrypt.Net-Next
   - Microsoft.AspNetCore.Diagnostics.HealthChecks

2. **Config DB:**
   Update `ConnectionStrings:DefaultConnection` in `src/MDWAPI/appsettings.json` to point to your SQL Server.
   Make sure the account can create the database or pre-create it.

3. **Run:**
   ```bash
   cd src/MDWAPI
   dotnet restore
   dotnet run
   ```

4. **Swagger:**
   Navigate to `/swagger`.
   - First call `POST /api/Auth/login` with body:
     ```json
     { "Username":"admin", "Password":"P@ssw0rd!" }
     ```
   - Copy the token from the response and click **Authorize** → type `Bearer yourtoken`.
   - Now you can access protected endpoints like `GET /api/Shopee/ping`.

## Database

EF will `EnsureCreated()` on startup for the minimal schema. If you want migrations:
```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p src/MDWAPI/MDWAPI.csproj -s src/MDWAPI/MDWAPI.csproj
dotnet ef database update -p src/MDWAPI/MDWAPI.csproj -s src/MDWAPI/MDWAPI.csproj
```

**Tables:** `Users`, `UserTokens`

## Security Notes
- Tokens are random, URL-safe Base64 strings (not JWT). Keep them secret, rotate often.
- Adjust `Auth:TokenLifetimeMinutes`.
- Replace the demo admin password and manage users securely.
- Consider adding refresh/rotate flows and audit logging.

## Next Steps (Shopee)
- Implement Partner API signing, time sync, and real endpoints.
- Add webhook receiver endpoints with signature validation.
- Store partner/shop credentials per user or per integration profile.
