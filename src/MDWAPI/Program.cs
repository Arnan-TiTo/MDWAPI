using MDWAPI.Auth;
using MDWAPI.Data;
using MDWAPI.Entities;
using MDWAPI.Helpers;
using MDWAPI.Models;
using MDWAPI.Repos;
using MDWAPI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Net;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// === Register Encoding ===
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

// ===== DATABASE =====
var conn = Environment.GetEnvironmentVariable("APP_DB")
//  ?? "Server=10.10.14.103,1433;Database=VCINDW;User Id=dev_mdw;Password=SW!TKy9$d5i;TrustServerCertificate=True;";
 ?? "Server=localhost;Database=VCINDW;User Id=sa;Password=Admin@9999;TrustServerCertificate=True;";


builder.Services.AddDbContext<AppDbContext>(
    opt => opt.UseSqlServer(conn),
    contextLifetime: ServiceLifetime.Scoped,
    optionsLifetime: ServiceLifetime.Singleton);




// ===== AUTH =====
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "DbToken";
        options.DefaultChallengeScheme = "DbToken";
    })
    .AddScheme<AuthenticationSchemeOptions, DbTokenAuthenticationHandler>("DbToken", _ => { });

builder.Services.AddAuthorization();

// ===== CACHING =====
builder.Services.AddMemoryCache();

// ===== REPOS & SERVICES =====
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<IShopRepo, ShopRepo>();
builder.Services.AddScoped<IPartnerRepo, PartnerRepo>();
builder.Services.AddScoped<IChannelTokenRepo, ChannelTokenRepo>();

builder.Services.AddScoped<ChannelTokenResolver>();

builder.Services.AddScoped<ShopeeAuthProvider>();
builder.Services.AddScoped<LazadaAuthProvider>();
builder.Services.AddScoped<TiktokAuthProvider>();

builder.Services.AddScoped<MarketplaceAuthService>();
builder.Services.AddScoped<ShopeeAuthLinkService>();
builder.Services.AddTransient<LazadaAuthLinkService>();
builder.Services.AddTransient<TiktokAuthLinkService>();

builder.Services.AddScoped<ShopeeOrderService>();
builder.Services.AddScoped<LazadaOrderService>();
builder.Services.AddScoped<TiktokOrderService>();
builder.Services.AddHttpClient<TikTokAuthService>();

builder.Services.AddScoped<ShopeeTokenRefreshService>();
builder.Services.AddScoped<IShopeeOrderIngestRepo, ShopeeOrderIngestRepo>();
builder.Services.AddScoped<ShopeeOrderIngestService>();

builder.Services.AddScoped<ShopeeLogisticsService>();
builder.Services.AddScoped<LazadaLogisticsService>();
builder.Services.AddScoped<TiktokLogisticsService>();

// UnifiedOrder normalize layer
builder.Services.AddScoped<IUnifiedOrderWriter, UnifiedOrderWriter>();
builder.Services.AddScoped<OrderNormalizeService>();
builder.Services.AddScoped<IIngestionAuditService, IngestionAuditService>();

// Auth token provider (internal - for cronjob)
builder.Services.AddSingleton<IAuthTokenProvider, InternalAuthTokenProvider>();

// ===== MEMBER LOYALTY SERVICES =====
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<MemberMappingService>();
builder.Services.AddScoped<PointPolicyEngine>();
builder.Services.AddScoped<PointService>();
builder.Services.AddScoped<RewardService>();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<EarnProcessingService>();
builder.Services.AddScoped<LineNotificationService>();
builder.Services.AddScoped<ReturnRefundSyncService>();
builder.Services.AddHttpClient<LineLoginService>();
builder.Services.AddHttpClient<LineWebhookService>();
builder.Services.AddScoped<ThailandAddressSeedService>();


// Background jobs
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<MarketJobHostedService>();
}
builder.Services.AddHostedService<EarnJobService>();
builder.Services.AddHostedService<OutboxProcessorService>();
builder.Services.AddScoped<PointExpiryProcessingService>();
builder.Services.AddHostedService<PointExpiryJobService>();
builder.Services.AddScoped<PointMaturationProcessingService>();
builder.Services.AddHostedService<PointMaturationJobService>();

// ===== HTTP CLIENTS =====
builder.Services.AddHttpClient("Shopee", c => { c.Timeout = TimeSpan.FromSeconds(30); });
builder.Services.AddHttpClient("Lazada", c => { c.Timeout = TimeSpan.FromSeconds(30); });
builder.Services.AddHttpClient("TikTok", c => { c.Timeout = TimeSpan.FromSeconds(30); })
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var host = config["Proxy:Host"];
        var port = config.GetValue<int>("Proxy:Port");

        if (!string.IsNullOrWhiteSpace(host) && port > 0)
        {
            var proxy = new WebProxy(host, port);
            var user = config["Proxy:Username"];
            var pass = config["Proxy:Password"];

            if (!string.IsNullOrWhiteSpace(user))
            {
                proxy.Credentials = new NetworkCredential(user, pass);
            }

            return new SocketsHttpHandler
            {
                Proxy = proxy,
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
        }

        return new SocketsHttpHandler();
    });

builder.Services.AddHttpClient("TikTokAuth", c =>
{
    c.BaseAddress = new Uri("https://auth.tiktok-shops.com");
    c.Timeout = TimeSpan.FromSeconds(30);
});

// สำหรับติดต่อ OrdersApi (ตั้ง BaseAddress จาก env/appsettings) + จูน handler
builder.Services.AddHttpClient("OrdersApi", (sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = Environment.GetEnvironmentVariable("ORDERS_BASE_URL")
                  ?? cfg.GetValue<string>("OrdersApi:BaseUrl")
                  ?? "https://madewe.vibeandchic.com";
                  //?? "https://localhost:7192";

    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(90);
    http.DefaultRequestHeaders.Accept.Clear();
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

#if DEBUG
    // เฉพาะ dev/local: อนุญาต self-signed ของ https://localhost:7192
    handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
    {
        RemoteCertificateValidationCallback = (msg, cert, chain, errors) => true
    };
#endif

    return handler;
});

// Controllers + จูน JSON (optional)
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
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "MDWAPI", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "Enter token as: Bearer {your-token}",
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
    });
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p
        .WithOrigins("https://vibeandchic.com",
                     "https://www.vibeandchic.com",
                     "https://madewe.vibeandchic.com",
                     "https://adwportal.vibeandchic.com",
                     "https://localhost:5254",
                     "http://localhost:5030",
                     "https://localhost:7156",
                     "http://localhost:5000",
                     "https://localhost:5001",
                     "https://liff.line.me")
        .WithMethods("POST", "GET", "PUT", "PATCH", "DELETE", "OPTIONS")
        .AllowAnyHeader()
    );
    options.AddPolicy("CallbackCors", p => p
        .WithOrigins("https://vibeandchic.com",
                     "https://adwportal.vibeanchic.com",
                     "https://localhost:5254",
                     "https://liff.line.me")
        .WithMethods("POST", "GET", "PUT", "DELETE", "OPTIONS")
        .AllowAnyHeader()
    );
});


// ===== HEALTH CHECKS =====TikTok needs accountIdStr (shop_id)
builder.Services.AddHealthChecks();

var app = builder.Build();

//// seed admin
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    if (!db.Users.Any(u => u.Username == "admin"))
    {
        db.Users.Add(new User
        {
            Username = "admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456yjm")
        });
        db.SaveChanges();
    }

    var addressSeed = scope.ServiceProvider.GetRequiredService<ThailandAddressSeedService>();
    addressSeed.SeedAsync().Wait();
}


if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// ถ้ารันหลัง reverse proxy ให้เปิด ForwardedHeaders (ปรับได้ตาม infra)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (builder.Configuration.GetValue<bool>("SwaggerEnabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");

app.UseHttpsRedirection();
app.UseCors();

// Static files for LIFF Mini App
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
