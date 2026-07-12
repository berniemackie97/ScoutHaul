using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using HaulScout.Api.Configuration;
using HaulScout.Api.Middleware;
using HaulScout.Api.Services;
using HaulScout.Application;
using HaulScout.Infrastructure;
using HaulScout.Infrastructure.Persistence;

// Load .env into environment variables for local dev BEFORE the config builder reads them.
// No-op in production (no .env file present); never overrides platform-provided vars.
if (!string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
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

SupabaseSigningKeyResolver signingKeyResolver = new(supabaseAuth);
builder.Services.AddSingleton(supabaseAuth);
builder.Services.AddSingleton(signingKeyResolver);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = supabaseAuth.Issuer,
            ValidateAudience = true,
            ValidAudience = supabaseAuth.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            IssuerSigningKeyResolver = signingKeyResolver.ResolveSigningKeys
        };
    });

builder.Services.AddScoped<OrganizationAccessService>();
builder.Services.AddScoped<MarketplaceLookupPersistenceService>();
builder.Services.AddScoped<InventoryPhotoStorageService>();
builder.Services.AddScoped<InventoryReferenceDataService>();
builder.Services.AddScoped<OrderAssembler>();
builder.Services.AddScoped<MarketplaceRefundImportService>();
builder.Services.AddScoped<PlatformOrderFeeEnrichmentService>();
builder.Services.AddScoped<PlatformOrderSuggestionService>();
builder.Services.AddScoped<PlatformOrderIngestionService>();
builder.Services.AddHttpClient<SupabaseAdminInviteService>();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddScoped<ICurrentUserContext, JwtCurrentUserContext>();

// ── Rate Limiting ────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

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
