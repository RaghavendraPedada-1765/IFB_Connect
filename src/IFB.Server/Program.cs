using IFB.Server.Auth;
using IFB.Server.Data;
using IFB.Server.Hubs;
using IFB.Server.Middleware;
using IFB.Server.Repositories;
using IFB.Server.Services;
using IFB.Server.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Security.Cryptography;
using System.Threading.RateLimiting;


// ── Bootstrap Serilog early ────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
    var builder = WebApplication.CreateBuilder(args);

    // Windows Service support
    builder.Host.UseWindowsService(options => options.ServiceName = "IFBConnectServer");

    // Serilog from appsettings.json
    builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

    // ── Configuration ────────────────────────────────────────────────────────
    var config = builder.Configuration;
    LanBinding.Configure(builder);

    // ── Database ─────────────────────────────────────────────────────────────
    var dbProvider = config["Database:Provider"] ?? "SQLite";
    var connStr = dbProvider == "SqlServer"
        ? config["Database:ConnectionStrings:SqlServer"]!
        : config["Database:ConnectionStrings:SQLite"]!;

    if (dbProvider == "SqlServer")
    { builder.Services.AddDbContext<SqlServerDbContext>(opts => opts.UseSqlServer(connStr));
      builder.Services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<SqlServerDbContext>()); }
    else if (dbProvider == "SQLite") builder.Services.AddDbContext<AppDbContext>(opts => opts.UseSqlite(connStr));
    else throw new InvalidOperationException("Database:Provider must be SQLite or SqlServer.");

    // ── JWT Auth ─────────────────────────────────────────────────────────────
    var keys = new JwtKeyStore(config, builder.Environment);
    builder.Services.AddSingleton(keys);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = config["Jwt:Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(keys.SigningKey),
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            // Support JWT in SignalR query string
            opts.Events = new JwtBearerEvents
            {
                OnTokenValidated = async ctx =>
                {
                    var id = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    var version = ctx.Principal?.FindFirst("token_version")?.Value;
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                    if (user == null || !user.IsActive || user.TokenVersion.ToString() != version) ctx.Fail("Session unavailable.");
                },
                OnMessageReceived = ctx =>
                {
                    var token = ctx.Request.Query["access_token"];
                    var path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                        ctx.Token = token;
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization(opts =>
    {
        opts.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
        opts.AddPolicy("ManagerOrAdmin", p => p.RequireRole("Manager", "Admin"));
    });
    if (config.GetValue("Auth:WindowsIntegrated:Enabled", false))
        builder.Services.AddAuthentication().AddNegotiate();

    // ── Rate Limiting ─────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(opts =>
    {
        var msgPerMin = int.Parse(config["RateLimiting:MessagesPerMinutePerUser"] ?? "60");
        opts.AddTokenBucketLimiter("messages", o =>
        {
            o.TokenLimit = msgPerMin;
            o.TokensPerPeriod = msgPerMin;
            o.ReplenishmentPeriod = TimeSpan.FromMinutes(1);
            o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            o.QueueLimit = 0;
        });

        var loginPerMin = int.Parse(config["RateLimiting:LoginAttemptsPerMinute"] ?? "5");
        opts.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMin,
                Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        opts.RejectionStatusCode = 429;
    });

    // ── SignalR ───────────────────────────────────────────────────────────────
    builder.Services.AddSignalR(opts =>
    {
        opts.AddFilter<ChatPolicyFilter>();
        opts.EnableDetailedErrors = builder.Environment.IsDevelopment();
        opts.MaximumReceiveMessageSize = 1024 * 64; // 64 KB for text messages
        opts.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        opts.KeepAliveInterval = TimeSpan.FromSeconds(15);
    });
    builder.Services.AddSingleton<ChatPolicyFilter>();

    // ── CORS (LAN only) ──────────────────────────────────────────────────────
    builder.Services.AddCors(opts =>
    {
        opts.AddDefaultPolicy(p => p
            .WithOrigins(
                "https://localhost:5001",
                $"https://{config["Discovery:ServerHost"]}:{config["Discovery:ServerPort"]}")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
    });

    // ── Application Services ──────────────────────────────────────────────────
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IMessageService, MessageService>();
    builder.Services.AddScoped<IConversationService, ConversationService>();
    builder.Services.AddScoped<IAuditService, AuditService>();

    // ── Repositories ──────────────────────────────────────────────────────────
    builder.Services.AddScoped<IMessageRepository, MessageRepository>();
    builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
    builder.Services.AddScoped<IAuditRepository, AuditRepository>();

    // Singleton presence tracker (in-memory, swap for Redis in large deployments)
    builder.Services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();

    // Background workers
    builder.Services.AddHostedService<UdpDiscoveryWorker>();
    builder.Services.AddHostedService<RetentionWorker>();
    builder.Services.AddHostedService<OfflineQueueWorker>();

    // HTTP context accessor for audit logging
    builder.Services.AddHttpContextAccessor();

    builder.Services.AddControllers(options => options.Filters.Add<AuditFilter>());
    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();
    app.UseExceptionHandler();

    // Auto-apply migrations on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        if (dbProvider == "SQLite") await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await DatabaseBootstrap.InitializeAsync(db, config);
    }

    // Middleware order matters!
    app.UseSerilogRequestLogging();
    app.UseMiddleware<LanGuardMiddleware>();    // IP allowlist — must be first
    app.UseHttpsRedirection();
    app.UseCors();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<ChatHub>("/hubs/chat", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", product = "IFBConnect" }));

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "IFB Connect Server terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
