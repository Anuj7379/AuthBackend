using AuthApi.Data;
using AuthApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configure port for Render deployment
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

var databaseProvider =
    builder.Configuration["DatabaseProvider"]
    ?? (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DATABASE_URL"))
        ? "InMemory"
        : "Postgres");

var defaultPostgresConnection =
    builder.Configuration.GetConnectionString("PostgresConnection")
    ?? builder.Configuration["PostgresConnection"]
    ?? string.Empty;

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

if (!string.IsNullOrWhiteSpace(databaseUrl))
{
    defaultPostgresConnection = ConvertDatabaseUrlToConnectionString(databaseUrl);
}

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetBytes(jwtKey).Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be configured and at least 32 bytes long. Update appsettings or environment variables with a stronger secret.");
}

if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
    && string.IsNullOrWhiteSpace(defaultPostgresConnection))
{
    throw new InvalidOperationException(
        "Postgres provider selected but no PostgresConnection or DATABASE_URL is configured.");
}

static string ConvertDatabaseUrlToConnectionString(string databaseUrl)
{
    if (string.IsNullOrWhiteSpace(databaseUrl))
    {
        return databaseUrl;
    }

    if (!databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return databaseUrl;
    }

    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    var connectionBuilder = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = userInfo.Length > 0 ? userInfo[0] : string.Empty,
        Password = userInfo.Length > 1 ? userInfo[1] : string.Empty,
        SslMode = Npgsql.SslMode.Require
    };

    if (!string.IsNullOrWhiteSpace(uri.Query))
    {
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;

            var key = pair[0].ToLowerInvariant();
            var value = Uri.UnescapeDataString(pair[1]);

            switch (key)
            {
                case "sslmode":
                    connectionBuilder.SslMode = value.ToLowerInvariant() switch
                    {
                        "disable" => Npgsql.SslMode.Disable,
                        "prefer" => Npgsql.SslMode.Prefer,
                        "require" => Npgsql.SslMode.Require,
                        "verify-ca" => Npgsql.SslMode.VerifyCA,
                        "verify-full" => Npgsql.SslMode.VerifyFull,
                        _ => connectionBuilder.SslMode
                    };
                    break;
                default:
                    try
                    {
                        connectionBuilder[key] = value;
                    }
                    catch
                    {
                        // Ignore unsupported query parameters
                    }
                    break;
            }
        }
    }

    return connectionBuilder.ConnectionString;
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Configure Forwarded Headers for Render proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddOpenApi();
builder.Services.AddControllers();

var allowedOrigins = builder.Configuration["AllowedOrigins"]?
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(o => o.TrimEnd('/'))
    .ToArray()
    ?? new[] { "http://localhost:4200" };

if (allowedOrigins.Length == 0)
{
    allowedOrigins = new[] { "http://localhost:4200" };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularClient", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddScoped<JwtService>();

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(defaultPostgresConnection);
    }
    else
    {
        options.UseInMemoryDatabase("AuthApi");
    }
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
            )
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["jwt"];
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Enable Forwarded Headers Middleware at top of pipeline
app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        dbContext.Database.Migrate();
    }
    else
    {
        dbContext.Database.EnsureCreated();
    }
}

// Health Check / Root Endpoint for Render deployment checks
app.MapGet("/", () => Results.Ok(new { message = "AuthApi Service is operational", status = "Healthy" }));

if (app.Environment.IsDevelopment() || string.Equals(builder.Configuration["ENABLE_SWAGGER"], "true", StringComparison.OrdinalIgnoreCase))
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AngularClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
