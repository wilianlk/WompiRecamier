using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using WompiRecamier.Models;
using WompiRecamier.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ContentRootPath = AppContext.BaseDirectory, // Establece la ra�z del contenido seg�n el directorio base
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") // Ruta expl�cita a wwwroot
});

// Configuraci�n de Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10)
    .CreateLogger();

builder.Host.UseSerilog();

// Configuraci�n JWT
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true
    };
});

// Configuraci�n de Rate Limiting
builder.Services.AddOptions();
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddHttpContextAccessor();

// Configuración de Wompi: lee "Wompi:ApiBaseUrl" de appsettings según entorno
builder.Services.Configure<WompiOptions>(
    builder.Configuration.GetSection("Wompi")
);

// Agregar servicios y controladores
builder.Services.AddControllers();
builder.Services.AddSingleton<InformixService>();

// Configuraci�n de Swagger con Autenticaci�n JWT
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Autorizaci�n JWT usando el esquema Bearer. \r\n\r\n Introduzca el token en el siguiente formato: 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"C:\DataProtection-Keys"))
    .ProtectKeysWithDpapi()
    .SetApplicationName("WompiRecamier");

// Configuraci�n de CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins("http://192.168.20.30:8083", "https://portalpagos.recamier.com", "http://localhost:3000") // Cambia por tus dominios permitidos
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Middleware de Swagger
app.UseSwagger();
if (app.Environment.IsProduction())
{
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WompiRecamier API v1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseSwaggerUI();
}

// Middleware de CORS
if (app.Environment.IsProduction())
{
    app.UseCors("AllowSpecificOrigins");
}
else
{
    app.UseCors("AllowAllOrigins"); // En desarrollo, permitir cualquier origen
}

// Middleware para servir el frontend
app.UseDefaultFiles(); // Redirige autom�ticamente a index.html
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        Console.WriteLine($"Sirviendo archivo est�tico: {ctx.File.PhysicalPath}");
    }
});

// Mapear rutas de API
app.MapControllers();

// Redirecci�n para React Router
app.MapFallbackToFile("index.html");

// Controlador de errores global
app.UseExceptionHandler("/error");
app.Map("/error", (HttpContext context) =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    return Results.Problem(detail: exception?.Message, title: "Error interno del servidor");
});

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
