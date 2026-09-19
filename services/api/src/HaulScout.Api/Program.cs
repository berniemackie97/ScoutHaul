using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Storefront;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using HaulScout.Api;
using HaulScout.Api.Authentication;
using HaulScout.Api.Configuration;
using HaulScout.Api.Middleware;
using HaulScout.Api.Services;
using HaulScout.Application;
using HaulScout.Infrastructure;
using HaulScout.Infrastructure.Persistence;

// Load .env into environment variables before the config builder reads them.
// Production cloud hosts inject real variables and ship no .env files, so this is a no-op there.
// Local profile overrides are still allowed explicitly through HAULSCOUT_ENV/HAULSCOUT_ENV_FILE.
bool hasExplicitLocalProfile =
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAULSCOUT_ENV")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAULSCOUT_ENV_FILE"));

if (hasExplicitLocalProfile ||
    !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
{
    DotEnv.LoadForDevelopment(Directory.GetCurrentDirectory());
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── Serialization ────────────────────────────────────────────────
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ── Swagger / Health / Problem Details ───────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();

// ── Authentication ───────────────────────────────────────────────
SupabaseAuthOptions supabaseAuth = SupabaseAuthOptions.FromConfiguration(builder.Configuration)
    ?? throw new InvalidOperationException("Authentication:Supabase:Url is required.");
SupabaseAuthOptions storefrontSupabaseAuth =
    SupabaseAuthOptions.FromConfiguration(builder.Configuration, "Authentication:StorefrontSupabase")
    ?? supabaseAuth;

SupabaseSigningKeyResolver signingKeyResolver = new(supabaseAuth);
SupabaseSigningKeyResolver storefrontSigningKeyResolver = new(storefrontSupabaseAuth);
builder.Services.AddSingleton(supabaseAuth);
builder.Services.AddSingleton(signingKeyResolver);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => SupabaseBearerConfiguration.Configure(options, supabaseAuth, signingKeyResolver))
    .AddJwtBearer(
        StorefrontAuthDefaults.Scheme,
        options => SupabaseBearerConfiguration.Configure(
            options, storefrontSupabaseAuth, storefrontSigningKeyResolver))
    // Paired browsers (the sync extension) authenticate with a device token, never a person's session.
    .AddScheme<AuthenticationSchemeOptions, SyncAgentAuthenticationHandler>(SyncAgentAuthDefaults.Scheme, configureOptions: null);

builder.Services.AddApiAdapters(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ── Rate Limiting ────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Pairing is the one anonymous sync endpoint, and a pairing code is short enough to type, so
    // guesses are held to a handful a minute per address.
    options.AddPolicy(SyncAgentAuthDefaults.PairingRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        string partitionKey = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User?.FindFirst("sub")?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60)),
                QueueLimit = 0,
            });
    });
});

// ── CORS ─────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod();

        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin =>
                origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://192.168.", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://10.", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("exp://", StringComparison.OrdinalIgnoreCase));
            return;
        }

        string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins);
    });
});

// ── Application / Infrastructure ─────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ═════════════════════════════════════════════════════════════════
WebApplication app = builder.Build();

// ── Exception Handling ───────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
    app.UseHttpsRedirection();
}

// ── Security Headers ─────────────────────────────────────────────
app.UseSecurityHeaders();
string webRootPath = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(webRootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webRootPath),
    RequestPath = string.Empty
});

// ── Database Migration ───────────────────────────────────────────
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    using IServiceScope scope = app.Services.CreateScope();
    HaulScoutDbContext db = scope.ServiceProvider.GetRequiredService<HaulScoutDbContext>();
    db.Database.Migrate();

    // The storefront shelf self-heals on every boot: rows that predate the save-time synchronizer (or were
    // written around EF) get their listings built here, so a deploy never depends on someone remembering a
    // manual reconcile call.
    await StoreShelfBootstrap.ReconcileAllAsync(
        db,
        scope.ServiceProvider.GetRequiredService<TimeProvider>(),
        app.Logger);

    // Buyers recorded twice before imports learned to recognise an existing customer are folded back
    // together here, for the same reason: the data should be right without anyone remembering to fix it.
    await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, app.Logger);
}

// ── Swagger (dev only) ───────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}
else
{
    app.MapGet("/", () => Results.Ok(new { service = "HaulScout API", status = "ok" })).AllowAnonymous();
}

// ── Middleware Pipeline ──────────────────────────────────────────
app.UseRateLimiter();
app.UseCors("Default");
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (UnauthorizedAccessException exception) when (!context.Response.HasStarted)
    {
        bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        int statusCode = isAuthenticated
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status401Unauthorized;
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = statusCode,
            Title = isAuthenticated ? "Forbidden" : "Unauthorized",
            Detail = exception.Message,
            Type = isAuthenticated
                ? "https://tools.ietf.org/html/rfc9110#section-15.5.4"
                : "https://tools.ietf.org/html/rfc9110#section-15.5.2"
        });
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.Run();

/// <summary>Visible to the HTTP tests, which host the real pipeline in memory.</summary>
public partial class Program;
