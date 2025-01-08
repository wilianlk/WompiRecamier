using Microsoft.AspNetCore.Authorization;
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

        //[Authorize]
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

        [HttpGet("validate-customer/{resal}")]
        public async Task<IActionResult> ValidateCustomer(string resal)
        {
            _logger.LogInformation($"Iniciando validación del cliente con resal: {resal}");

            try
            {
                var (exists, status_type) = await _informixService.ValidateCustomerAsync(resal);

                if (!exists)
                {
                    _logger.LogWarning($"Cliente con resal {resal} no encontrado en la base de datos.");
                    return NotFound(new
                    {
                        status = "NotFound",
                        resal = resal,
                        message = "El cliente no existe en la base de datos."
                    });
                }

                _logger.LogInformation($"Cliente con resal {resal} encontrado. Status: {status_type}");
                return Ok(new
                {
                    status = "Success",
                    resal = resal,
                    message = "El cliente existe en la base de datos.",
                    customerStatus = status_type
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al validar el cliente con resal {resal}");
                return StatusCode(500, new
                {
                    status = "Error",
                    resal = resal,
                    message = "Ocurrió un error al validar el cliente."
                });
            }
        }

        [HttpGet("get-customer-details/{resal}")]
        public async Task<IActionResult> GetCustomerDetailsAsync(string resal)
        {
            _logger.LogInformation("Iniciando solicitud para obtener información del cliente con Resal: {Resal}", resal);

            try
            {
                var customerDetails = await _informixService.GetCustomerDetailsAsync(resal);

                if (customerDetails == null)
                {
                    _logger.LogWarning("No se encontró información del cliente para Resal: {Resal}", resal);
                    return NotFound(new
                    {
                        Status = "NotFound",
                        Resal = resal,
                        Message = "No se encontró información del cliente."
                    });
                }

                _logger.LogInformation("Cliente encontrado: {CustomerName}, Teléfono: {Telephone}", customerDetails.Name, customerDetails.Telephone);
                return Ok(new
                {
                    Status = "Success",
                    Resal = resal,
                    CustomerDetails = customerDetails,
                    Message = "Información del cliente obtenida exitosamente."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar la solicitud para Resal: {Resal}", resal);
                return StatusCode(500, new
                {
                    Status = "Error",
                    Resal = resal,
                    Message = $"Error al procesar la solicitud: {ex.Message}"
                });
            }
        }

        [HttpGet("customer-payments/{customer}")]
        public async Task<IActionResult> GetCustomerPayments(string customer)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(customer))
                {
                    return BadRequest("Los parámetros 'customer' son obligatorios.");
                }

                // Llama al método para obtener los detalles de pagos
                var invoiceDetails = await _informixService.GetInvoiceDetailsAsync(customer);

                // Si no hay resultados, devuelve un mensaje adecuado
                if (invoiceDetails == null || !invoiceDetails.Any())
                {
                    return NotFound(new
                    {
                        Status = "NotFound",
                        Customer = customer,
                        Message = $"No se encontraron pagos para el cliente."
                    });
                }

                // Devuelve los resultados en formato JSON
                return Ok(invoiceDetails);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener pagos del cliente: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Customer = customer,
                    Message = "Ocurrió un error interno al procesar la solicitud."
                });
            }
        }
    }
}
