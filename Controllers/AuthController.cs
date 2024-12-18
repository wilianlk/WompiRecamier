using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace WompiRecamier.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Endpoint para validar credenciales y generar un token
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginModel login)
        {
            // Valida credenciales (esto es solo un ejemplo, en la vida real usa una base de datos)
            if (login.Username == "admin" && login.Password == "password")
            {
                // Configuración JWT
                var jwtSettings = _configuration.GetSection("Jwt");
                var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Name, login.Username),
                        new Claim(ClaimTypes.Role, "Admin") // Ejemplo de un rol
                    }),
                    Expires = DateTime.UtcNow.AddMinutes(Convert.ToDouble(jwtSettings["ExpiryMinutes"])),
                    Issuer = jwtSettings["Issuer"],
                    Audience = jwtSettings["Audience"],
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateToken(tokenDescriptor);

                return Ok(new
                {
                    Token = tokenHandler.WriteToken(token),
                    Expiration = tokenDescriptor.Expires
                });
            }

            // Si las credenciales no son válidas
            return Unauthorized(new { Message = "Credenciales incorrectas" });
        }
    }

    // Modelo para recibir las credenciales del usuario
    public class LoginModel
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
