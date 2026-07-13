using Microsoft.EntityFrameworkCore;
using OpenSearch.Client;
using Scalar.AspNetCore;
using SearchGateway.Data;
using SearchGateway.Routes;
using SearchGateway.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Postgres DB Context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// OpenSearch Configuration
var openSearchUrl = builder.Configuration.GetValue<string>("OpenSearch:Url") ?? "http://localhost:9200";
var connectionSettings = new ConnectionSettings(new Uri(openSearchUrl))
    .DefaultIndex("movies")
    .DisableDirectStreaming();

builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(connectionSettings));
builder.Services.AddSingleton<OpenSearchService>();
builder.Services.AddHttpClient<VespaService>(client =>
{
    var url = builder.Configuration.GetValue<string>("Vespa:Url") ?? "http://localhost:8080/";
    client.BaseAddress = new Uri(url);
});
builder.Services.AddScoped<DatabaseSeeder>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseDefaultFiles();
app.UseStaticFiles();

// if (!app.Environment.IsDevelopment())
// {
//     app.UseHttpsRedirection();
// }

// Automatically ensure the database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        // For development, conditionally drop database if requested via CLI arg
        if (args.Contains("--reset-db"))
        {
            Console.WriteLine("Force resetting database schema...");
            dbContext.Database.EnsureDeleted();
        }

        dbContext.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error verifying/creating database on startup: {ex.Message}");
    }
}

// Map endpoints using Extension Methods
app.MapDatabaseEndpoints();
app.MapSearchEndpoints();

app.Run();

