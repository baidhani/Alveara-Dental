var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

const string LocalClientCorsPolicy = "LocalClientCorsPolicy";

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// The client is a separate Vite dev server during development. Production
// deployment serves the built client from the same origin as this API, at
// which point this policy is not exercised, but it is kept scoped to
// localhost origins only rather than a wildcard.
builder.Services.AddCors(options =>
{
    options.AddPolicy(LocalClientCorsPolicy, policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(LocalClientCorsPolicy);

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so the API's test project can spin up an in-memory instance
// without duplicating Program.cs.
public partial class Program { }
