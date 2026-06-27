using AuthApi.Data;
using AuthApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

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
    var postgresBuilder = new Npgsql.NpgsqlConnectionStringBuilder(databaseUrl)
    {
        SslMode = Npgsql.SslMode.Require
    };

    defaultPostgresConnection = postgresBuilder.ToString();
}

if (databaseProvider.Equals(
        "Postgres",
        StringComparison.OrdinalIgnoreCase)
    && string.IsNullOrWhiteSpace(defaultPostgresConnection))
{
    throw new InvalidOperationException(
        "Postgres provider selected but no PostgresConnection or DATABASE_URL is configured.");
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
var allowedOrigins = builder.Configuration["AllowedOrigins"]?
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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

var dataProtectionKeysPath =
    Path.Combine(
        builder.Environment.ContentRootPath,
        "DataProtectionKeys");

Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(dataProtectionKeysPath));

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (databaseProvider.Equals(
        "Postgres",
        StringComparison.OrdinalIgnoreCase))
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
    options.TokenValidationParameters =
        new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],

            IssuerSigningKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(
                        builder.Configuration["Jwt:Key"]!
                    )
                )
        };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            context.Token =
                context.Request.Cookies["jwt"];

            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext =
        scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (databaseProvider.Equals(
        "Postgres",
        StringComparison.OrdinalIgnoreCase))
    {
        dbContext.Database.Migrate();
    }
    else
    {
        dbContext.Database.EnsureCreated();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
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
