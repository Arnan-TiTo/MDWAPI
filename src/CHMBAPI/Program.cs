using CHMBAPI.Data;
using CHMBAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// === Register Encoding ===
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

// ===== DATABASE =====
var conn = Environment.GetEnvironmentVariable("APP_DB")
//    ?? "Server=10.10.14.103,1433;Database=VCINDW;User Id=dev_mdw;Password=SW!TKy9$d5i;TrustServerCertificate=True;";
 ?? "Server=localhost;Database=VCINDW;User Id=sa;Password=Admin@9999;TrustServerCertificate=True;";

builder.Services.AddDbContext<AppDbContext>(
    opt => opt.UseSqlServer(conn),
    contextLifetime: ServiceLifetime.Scoped,
    optionsLifetime: ServiceLifetime.Singleton);

// ===== AUTH =====
const string defaultFixedApiToken = "CHMBAPI_FE_2026_r8Kp2Vx7Lm4Qn9Td1Ys6Wb3Hc5Zj0Fa";
var fixedApiToken = Environment.GetEnvironmentVariable("CHMBAPI_FIXED_TOKEN")
    ?? defaultFixedApiToken;

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// ===== CACHING =====
builder.Services.AddMemoryCache();

// ===== MEMBER LOYALTY SERVICES =====
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<MemberMappingService>();
builder.Services.AddScoped<PointPolicyEngine>();
builder.Services.AddScoped<PointService>();
builder.Services.AddScoped<RewardService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<TierService>();
builder.Services.AddScoped<EarnProcessingService>();
builder.Services.AddScoped<LineNotificationService>();
builder.Services.AddScoped<ContentService>();

// Background jobs
builder.Services.AddHostedService<EarnJobService>();
builder.Services.AddScoped<PointExpiryProcessingService>();
builder.Services.AddHostedService<PointExpiryJobService>();
builder.Services.AddScoped<PointMaturationProcessingService>();
builder.Services.AddHostedService<PointMaturationJobService>();

// ===== HTTP CLIENTS =====
builder.Services.AddHttpClient<LineLoginService>();
builder.Services.AddHttpClient<LineWebhookService>();

// Controllers + JSON options
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = null;
        o.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// ===== SWAGGER =====
if (builder.Configuration.GetValue<bool>("SwaggerEnabled"))
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "CHMBAPI - Member Loyalty", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "Enter fixed frontend token as: Bearer {your-token}",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "Token"
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            { new OpenApiSecurityScheme
                { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
              Array.Empty<string>() }
        });

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        }
    });
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p
        .WithOrigins("https://vibeandchic.com",
                     "https://www.vibeandchic.com",
                     "https://madewe.vibeandchic.com",
                     "https://localhost:5254",
                     "http://localhost:5030",
                     "https://localhost:7156",
                     "http://localhost:5000",
                     "https://localhost:5001",
                     "https://localhost:5010",
                     "https://liff.line.me",
                     "https://chicspheremember.vibeandchic.com",
                     "https://shp251.vibeandchic.com"
                     )
        .WithMethods("POST", "GET", "PUT", "PATCH", "DELETE", "OPTIONS")
        .AllowAnyHeader()
    );
});

// ===== HEALTH CHECKS =====
builder.Services.AddHealthChecks();

var app = builder.Build();

// Seed admin user
using (var scope = app.Services.CreateScope())
{
    Console.WriteLine("***** CHMBAPI - Member Loyalty Service Starting *****");
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

if (builder.Configuration.GetValue<bool>("SwaggerEnabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.DocumentTitle = "CHMBAPI Swagger";
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CHMBAPI v1");
        c.DisplayRequestDuration();
        c.RoutePrefix = "swagger";
    });
}

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

app.UseRouting();
app.UseHealthChecks("/health");

app.UseCors();

app.Use(async (context, next) =>
{
    if (HttpMethods.IsOptions(context.Request.Method))
    {
        await next();
        return;
    }

    var path = context.Request.Path;
    var requiresFixedToken =
        path.StartsWithSegments("/api") &&
        !path.StartsWithSegments("/health") &&
        !path.StartsWithSegments("/swagger") &&
        !path.StartsWithSegments("/api/line/callback");

    if (!requiresFixedToken)
    {
        await next();
        return;
    }

    var authHeader = context.Request.Headers.Authorization.ToString();
    var bearerToken = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authHeader["Bearer ".Length..].Trim()
        : null;
    var headerToken = context.Request.Headers["X-Api-Token"].ToString();
    var requestToken = !string.IsNullOrWhiteSpace(bearerToken) ? bearerToken : headerToken;

    if (!string.Equals(requestToken, fixedApiToken, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Invalid or missing API token"
        });
        return;
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, "0"),
        new Claim(ClaimTypes.Name, "FE_CLIENT")
    };

    context.User = new ClaimsPrincipal(
        new ClaimsIdentity(claims, "FixedToken"));

    await next();
});

app.UseAuthorization();

app.MapControllers();

app.Run();
