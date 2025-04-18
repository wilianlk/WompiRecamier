using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WompiRecamier.Services;
using WompiRecamier.Models;
using System.Globalization;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace WompiRecamier.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValidationController : ControllerBase
    {
        private readonly InformixService _informixService;
        private readonly ILogger<ValidationController> _logger;
        private readonly string _wompiBaseUrl;

        public ValidationController(InformixService informixService, ILogger<ValidationController> logger, IOptions<WompiOptions> wompiOptions)
        {
            _informixService = informixService;
            _logger = logger;
            _wompiBaseUrl = wompiOptions.Value.ApiBaseUrl;
        }

        [HttpGet("env-check")]
        public IActionResult GetEnvironment([FromServices] IWebHostEnvironment env)
        {
            return Ok(new
            {
                Environment = env.EnvironmentName,
                IsDevelopment = env.IsDevelopment()
            });
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
                    customerType = status_type
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

        [HttpGet("calculate-discount/{invoiceNumber}/{netValue}")]
        public async Task<IActionResult> CalculateDiscount(string invoiceNumber, decimal netValue)
        {
            try
            {
                _logger.LogInformation("Calculando pronto pago para la factura: {InvoiceNumber}", invoiceNumber);

                // Llamar al servicio sin pasar fechas (se generan dentro del servicio)
                decimal discount = await _informixService.CalculateDiscountAsync(invoiceNumber, netValue);

                return Ok(new
                {
                    status = "Success",
                    invoiceNumber = invoiceNumber,
                    netValue = netValue,
                    discount = discount,
                    message = "Cálculo de pronto pago realizado exitosamente."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al calcular el pronto pago para la factura: {InvoiceNumber}", invoiceNumber);
                return StatusCode(500, new { status = "Error", message = "Error interno al calcular el pronto pago." });
            }
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] JsonElement payload)
        {
            try
            {
                _logger.LogInformation("JSON recibido: {Payload}", payload.GetRawText());

                var webhook = JsonSerializer.Deserialize<WompiWebhook>(
                    payload.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (webhook?.Data?.Transaction?.Id == null || webhook.Data.Transaction.Status == null)
                {
                    _logger.LogWarning("Campos críticos faltantes en el webhook.");
                    return BadRequest(new { status = "ValidationError", message = "Campos críticos faltantes en el webhook." });
                }

                var t = webhook.Data.Transaction;
                _logger.LogInformation("Procesando transacción {TransactionId}, Estado: {Status}", t.Id, t.Status);

                // Se llama al servicio modificado que registra cada factura de forma independiente
                await _informixService.InsertTransferControlAsync(webhook);

                switch (t.Status)
                {
                    case "APPROVED":
                        _logger.LogInformation("Transacción aprobada: {Reference}", t.Reference);
                        break;
                    case "DECLINED":
                        _logger.LogWarning("Transacción declinada: {Reference}", t.Reference);
                        break;
                    case "ERROR":
                        _logger.LogError("Error en transacción: {Reference}, Mensaje {StatusMessage}", t.Reference, t.StatusMessage);
                        break;
                    default:
                        _logger.LogWarning("Estado no manejado: {Status}", t.Status);
                        return BadRequest(new { status = "UnhandledStatus", message = "Estado de transacción no soportado." });
                }

                return Ok(new { status = "Success", message = "Webhook procesado correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar el webhook.");
                return StatusCode(500, new { status = "Error", message = "Error interno al procesar el webhook." });
            }
        }

        [HttpGet("confirmation")]
        public async Task<IActionResult> Confirmation([FromQuery] string transactionId)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
                return BadRequest(new { status = "ValidationError", message = "transactionId es requerido." });

            try
            {
                var (status, data) = await _informixService
                    .HandleConfirmationAsync(transactionId, _wompiBaseUrl);

                return Ok(new { status, transactionId, data });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "WompiError en confirmation");
                return StatusCode(502, new { status = "WompiError", message = ex.Message });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error interno en confirmation");
                return StatusCode(500, new { status = "Error", message = "Error interno al procesar confirmation." });
            }
        }
    }
}
