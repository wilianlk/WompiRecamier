using AspNetCoreRateLimit; // Importa la librer�a de Rate Limiting
using Serilog;
using WompiRecamier.Services;

var builder = WebApplication.CreateBuilder(args);

// Configurar Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration) // Lee la configuraci�n desde appsettings.json
    .WriteTo.Console() // Logs en la consola
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10)
    .CreateLogger();

// Reemplaza el logger predeterminado de ASP.NET Core con Serilog
builder.Host.UseSerilog();

// Configuraci�n de Rate Limiting
builder.Services.AddOptions();
builder.Services.AddMemoryCache(); // Necesario para Rate Limiting
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Agregar servicios y controladores
builder.Services.AddControllers();
builder.Services.AddSingleton<InformixService>();

// Configuraci�n de Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Middleware de Rate Limiting
app.UseIpRateLimiting();

app.UseAuthorization();

app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicaci�n fall� al iniciar.");
}
finally
{
    Log.CloseAndFlush();
}
