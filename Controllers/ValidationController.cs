using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WompiRecamier.Services;

namespace WompiRecamier.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValidationController : ControllerBase
    {
        private readonly InformixService _informixService;
        private readonly ILogger<ValidationController> _logger;

        // Constructor que inyecta el servicio y el logger
        public ValidationController(InformixService informixService, ILogger<ValidationController> logger)
        {
            _informixService = informixService;
            _logger = logger;
        }

        // Endpoint para probar la conexión
        [HttpGet("test-connection")]
        public IActionResult TestDatabaseConnection()
        {
            _logger.LogInformation("Iniciando prueba de conexión a la base de datos.");

            bool isConnected = _informixService.TestConnection();

            if (isConnected)
            {
                _logger.LogInformation("Conexión exitosa a la base de datos.");
                return Ok("Conexión exitosa a la base de datos.");
            }
            else
            {
                _logger.LogError("Error al conectar a la base de datos.");
                return StatusCode(500, "Error al conectar a la base de datos.");
            }
        }

        // Endpoint para validar si un cliente existe
        [HttpGet("validate-customer/{resal}")]
        public async Task<IActionResult> ValidateCustomer(string resal)
        {
            _logger.LogInformation($"Iniciando validación del cliente con resal: {resal}");

            try
            {
                bool exists = await _informixService.ValidateCustomerAsync(resal);

                if (exists)
                {
                    _logger.LogInformation($"Cliente con resal {resal} encontrado en la base de datos.");
                    return Ok(new
                    {
                        Resal = resal,
                        Message = "El cliente existe en la base de datos."
                    });
                }
                else
                {
                    _logger.LogWarning($"Cliente con resal {resal} no encontrado en la base de datos.");
                    return NotFound(new
                    {
                        Resal = resal,
                        Message = "El cliente no existe en la base de datos."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al validar el cliente con resal {resal}");
                return StatusCode(500, "Ocurrió un error al validar el cliente.");
            }
        }

        // Endpoint para obtener pagos pendientes
        [HttpGet("customer-payments/{company}/{customer}")]
        public async Task<IActionResult> GetCustomerPayments(string company, string customer)
        {
            _logger.LogInformation($"Iniciando búsqueda de pagos pendientes para la compañía {company} y el cliente {customer}.");

            try
            {
                var payments = await _informixService.GetPendingPaymentsAsync(company, customer);

                if (payments.Any())
                {
                    _logger.LogInformation($"Pagos pendientes encontrados para el cliente {customer} en la compañía {company}.");
                    return Ok(new
                    {
                        Customer = customer,
                        Message = "Facturas y pagos pendientes encontrados.",
                        Data = payments
                    });
                }
                else
                {
                    _logger.LogWarning($"No se encontraron pagos pendientes para el cliente {customer} en la compañía {company}.");
                    return NotFound(new
                    {
                        Customer = customer,
                        Message = "No se encontraron pagos pendientes para este cliente."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener pagos pendientes para la compañía {company} y el cliente {customer}");
                return StatusCode(500, "Ocurrió un error al obtener los pagos pendientes.");
            }
        }
    }
}
