using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ScoutHaul.Api.Middleware;
using ScoutHaul.Api.Services;
using ScoutHaul.Application;
using ScoutHaul.Infrastructure;
using ScoutHaul.Infrastructure.Persistence;

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
bool useStubAuth = builder.Configuration.GetSection("StubAuth").Exists()
    && !string.IsNullOrWhiteSpace(builder.Configuration["StubAuth:DefaultOrganizationId"]);

if (useStubAuth)
{
    builder.Services.AddScoped<ICurrentUserContext, HeaderCurrentUserContext>();
}
else
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["Auth:Authority"];
            options.Audience = builder.Configuration["Auth:Audience"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
    builder.Services.AddScoped<ICurrentUserContext, JwtCurrentUserContext>();
}

// ── Rate Limiting ────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100),
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60)),
                QueueLimit = 0,
            }));
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

// ── Database Migration ───────────────────────────────────────────
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
{
    using IServiceScope scope = app.Services.CreateScope();
    ScoutHaulDbContext db = scope.ServiceProvider.GetRequiredService<ScoutHaulDbContext>();
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
    app.MapGet("/", () => Results.Ok(new { service = "ScoutHaul API", status = "ok" })).AllowAnonymous();
}

// ── Middleware Pipeline ──────────────────────────────────────────
app.UseRateLimiter();
app.UseCors("Default");

if (!useStubAuth)
{
    app.UseAuthentication();
}

app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.Run();
