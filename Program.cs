using System.Threading.RateLimiting;
using Kue.Api.Data;
using Kue.Api.Entities;
using Kue.Api.Extensions;
using Kue.Api.Middlewares;
using Kue.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// 1. Database (PostgreSQL)
// ---------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------------------------------------------------------------------
// 2. Authentication and Media services
// ---------------------------------------------------------------------
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<Kue.Api.Services.Media.IExternalMediaService, Kue.Api.Services.Media.ExternalMediaService>();
builder.Services.AddScoped<Kue.Api.Services.Media.IMediaService, Kue.Api.Services.Media.MediaService>();
builder.Services.AddScoped<Kue.Api.Services.Notifications.INotificationService, Kue.Api.Services.Notifications.NotificationService>();

// ---------------------------------------------------------------------
// 3. Security & Middleware
// ---------------------------------------------------------------------
builder.Services.AddCorsPolicy();
builder.Services.AddJwtAuthentication(builder.Configuration);

// ---------------------------------------------------------------------
// 4. Rate limiting
// ---------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit          = 5,
                TokensPerPeriod     = 5,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit          = 0,
                AutoReplenishment   = true
            }));

    options.AddPolicy("refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0
            }));
});

// ---------------------------------------------------------------------
// 5. Controllers / OpenAPI / SignalR
// ---------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();

var app = builder.Build();

// ---------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------
app.UseGlobalExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseCors(CorsExtensions.AllowFrontendPolicy);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<Kue.Api.Hubs.NotificationHub>("/hubs/notifications");

app.Run();