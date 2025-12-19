using IBM.Data.Db2;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using WompiRecamier.Models;
using System.Transactions;
using System.Linq;
using System.Text.RegularExpressions;

namespace WompiRecamier.Services
{
    public class InformixService
    {
        private readonly string _connectionString;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public InformixService(IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor)
        {
            Console.WriteLine($"Entorno actual: {env.EnvironmentName}");

            _connectionString = env.IsDevelopment()
                ? configuration.GetConnectionString("InformixConnection")
                : configuration.GetConnectionString("InformixConnectionProduction");

            _httpContextAccessor = httpContextAccessor;

            Console.WriteLine($"Cadena seleccionada: {_connectionString}");
        }
        public bool TestConnection()
        {
            try
            {
                using var connection = new DB2Connection(_connectionString);
                connection.Open();

                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al conectar a la base de datos: {ex.Message}");
                return false;
            }
        }
        public async Task<(bool Exists, string Status)> ValidateCustomerAsync(string resal)
        {
            try
            {
                using var connection = new DB2Connection(_connectionString);
                await connection.OpenAsync();

                using var validateCommand = new DB2Command("proc_validate_customer_count_wompy", connection);
                validateCommand.CommandType = CommandType.StoredProcedure;
                validateCommand.Parameters.Add(new DB2Parameter("p_resal", resal));
                validateCommand.Parameters.Add(new DB2Parameter("p_query_type", "C"));

                var result = await validateCommand.ExecuteScalarAsync();
                bool exists = Convert.ToInt32(result) > 0;

                if (!exists)
                    return (false, null);

                string statusQuery = @"
                    SELECT cwe_status_type
                    FROM custmain_wompi_excepcion
                    WHERE cwe_cmpy = 'RE'
                    AND (SUBSTR(cwe_resal, 2, 15) = @resal 
                    OR cwe_cust = @resal)";

                using var statusCommand = new DB2Command(statusQuery, connection);
                statusCommand.Parameters.Add(new DB2Parameter("@resal", resal));

                var status = (string)(await statusCommand.ExecuteScalarAsync()) ?? "A";

                return (true, status);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al ejecutar la consulta: {ex.Message}");
                throw;
            }
        }
        public async Task<CustomerDetails> GetCustomerDetailsAsync(string resal)
        {
            try
            {
                using var connection = new DB2Connection(_connectionString);
                await connection.OpenAsync();

                using var command = new DB2Command("proc_get_customer_details_wompy", connection);
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.Add(new DB2Parameter("p_resal", resal));

                var customerDetails = new CustomerDetails();

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    customerDetails.Name = reader["cm_name"] != DBNull.Value ? reader["cm_name"].ToString() : null;
                    customerDetails.Telephone = reader["cm_tele"] != DBNull.Value ? reader["cm_tele"].ToString() : null;
                }

                return customerDetails.Name == null && customerDetails.Telephone == null ? null : customerDetails;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al ejecutar la consulta: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return null;
            }
        }
        public async Task<List<PaymentInfo>> GetInvoiceDetailsAsync(string resal)
        {
            try
            {
                using var connection = new DB2Connection(_connectionString);
                await connection.OpenAsync();

                using var getCustomerCodeCommand = new DB2Command("proc_validate_customer_count_wompy", connection);
                getCustomerCodeCommand.CommandType = CommandType.StoredProcedure;
                getCustomerCodeCommand.Parameters.Add(new DB2Parameter("p_resal", resal));
                getCustomerCodeCommand.Parameters.Add(new DB2Parameter("p_query_type", "U"));

                var result = await getCustomerCodeCommand.ExecuteScalarAsync();
                string customerCode = result?.ToString();

                // Si no se encuentra el código de cliente, retorna una lista vacía
                if (string.IsNullOrEmpty(customerCode))
                {
                    Console.WriteLine("No se encontró un cliente para el código proporcionado.");
                    return new List<PaymentInfo>();
                }

                // Paso 2: Consulta principal para obtener detalles de las facturas
               /* string baseQuery = @"
                SELECT unique in_num, 
                       (xin_mont - xin_paga) AS valor_neto,
                       in_date,in_misc
                FROM invhead
                JOIN xinvhead ON xin_num = in_num AND xin_cmpy = in_cmpy
                LEFT JOIN guias on gui_cmpy = xin_cmpy and gui_inv = xin_num
                WHERE in_cmpy = 'RE'
                  AND in_cust = @CustomerCode
                  AND in_amt > in_paid
                  AND in_post != 'V'
                  AND xin_mont > xin_paga
                  AND gui_fentreal IS NOT NULL
                  AND (in_num NOT IN (
  	                    SELECT ctr_factura_numero
 	                    FROM control_transferencias
	                    WHERE ctr_pago_estado = 'APPROVED' 
                        AND ctr_factura_numero = in_num
                      )
                  OR in_num IN (
                      SELECT ctr_factura_numero
                      FROM control_transferencias
                      WHERE ctr_pago_estado = 'APPROVED' 
                      AND ctr_factura_numero = in_num
                      and length(ctr_pago_marca) != 0
                      ))
                  AND in_misc = 'FR'

                UNION ALL 

                SELECT in_num, 
                       (xin_mont - xin_paga) AS valor_neto,
                       in_date,in_misc
                FROM invhead
                JOIN xinvhead ON xin_num = in_num AND xin_cmpy = in_cmpy
                WHERE in_cmpy = 'RE'
                  AND in_cust = @CustomerCode
                  AND in_amt > in_paid
                  AND in_post != 'V'
                  AND xin_mont > xin_paga
                  AND (in_num NOT IN (
                          SELECT ctr_factura_numero
                          FROM control_transferencias
                          WHERE ctr_pago_estado = 'APPROVED' 
                          AND ctr_factura_numero = in_num
                      )
                      OR in_num IN (
                          SELECT ctr_factura_numero
                          FROM control_transferencias
                          WHERE ctr_pago_estado = 'APPROVED' 
                          AND ctr_factura_numero = in_num
                          and length(ctr_pago_marca) != 0
                      )
                   )
                 AND in_misc = 'DB'
                 ORDER BY in_misc,in_date,in_num";

                using var baseCommand = new DB2Command(baseQuery, connection);
                baseCommand.Parameters.Add(new DB2Parameter("@CustomerCode", customerCode));*/
                
                using var baseCommand = new DB2Command("proc_get_customer_invoices_wompy", connection);
                baseCommand.CommandType = CommandType.StoredProcedure;
                baseCommand.Parameters.Add(new DB2Parameter("p_customer_code", customerCode));
                baseCommand.CommandTimeout = 300;

                var paymentInfoList = new List<PaymentInfo>();

                using var baseReader = await baseCommand.ExecuteReaderAsync();
                while (await baseReader.ReadAsync())
                {
                    var invoiceNumber = baseReader["in_num"].ToString().Trim();
                    var invoiceValue = Convert.ToDecimal(baseReader["valor_factura"]);
                    var netValue = Convert.ToDecimal(baseReader["saldo"]);
                    var invoiceDate = Convert.ToDateTime(baseReader["in_date"]);
                    var miscInfo = baseReader["in_misc"].ToString();
                    var currentDate = DateTime.Now;
                    var receiptDate = currentDate.ToString("yyyy-MM-dd");
                    var checkDate = currentDate.ToString("MM/dd/yyyy");

                    // Segunda consulta para calcular el descuento
                    string derivedQuery = @"
                    SELECT COALESCE(proc_reglasprontopago2('RE', @InvoiceNumber, @ReceiptDate, @NetValue, @CheckDate), 0) AS DiscountResult
                    ";

                    using var derivedCommand = new DB2Command(derivedQuery, connection);
                    derivedCommand.Parameters.Add(new DB2Parameter("@InvoiceNumber", invoiceNumber));
                    derivedCommand.Parameters.Add(new DB2Parameter("@ReceiptDate", receiptDate));
                    derivedCommand.Parameters.Add(new DB2Parameter("@NetValue", netValue));
                    //derivedCommand.Parameters.Add(new DB2Parameter("@NetValue", invoiceValue));
                    derivedCommand.Parameters.Add(new DB2Parameter("@CheckDate", checkDate));


                    var discountResult = 0m;
                    using var derivedReader = await derivedCommand.ExecuteReaderAsync();
                    if (await derivedReader.ReadAsync())
                    {
                        discountResult = Convert.ToDecimal(derivedReader["DiscountResult"]);
                    }

                    // Consultar si hay transacción PENDING en el histórico
                    string pendingTransactionId = null;

                    string pendingQuery = @"
                    SELECT FIRST 1 ctr_transaccion_numero
                    FROM control_transferencias_historico
                    WHERE ctr_factura_numero = @Factura
                      AND ctr_pago_estado = 'PENDING'
                    ";

                    using var pendingCmd = new DB2Command(pendingQuery, connection);
                    pendingCmd.Parameters.Add(new DB2Parameter("@Factura", DB2Type.Integer)
                    {
                        Value = int.TryParse(invoiceNumber, out var num) ? num : 0
                    });

                    var resultPending = await pendingCmd.ExecuteScalarAsync();
                    if (resultPending != null)
                        pendingTransactionId = resultPending.ToString();

                    // Agregar los resultados al objeto final
                    paymentInfoList.Add(new PaymentInfo
                    {
                        InvoiceNumber = invoiceNumber,
                        NetValue = netValue,
                        InvoiceValue = invoiceValue,
                        DiscountResult = discountResult,
                        NetValueWithDiscount = netValue - discountResult,
                        InvoiceDate = invoiceDate,
                        MiscInfo = miscInfo,
                        TransactionId = pendingTransactionId
                    });
                }

                return paymentInfoList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return new List<PaymentInfo>();
            }
        }
        public async Task<decimal> CalculateDiscountAsync(string invoiceNumber, decimal netValue)
        {
            try
            {
                using var connection = new DB2Connection(_connectionString);
                await connection.OpenAsync();

                var currentDate = DateTime.Now;
                var receiptDate = currentDate.ToString("yyyy-MM-dd");  
                var checkDate = currentDate.ToString("MM/dd/yyyy");

                // Consulta para calcular el descuento con proc_reglasprontopago
                string derivedQuery = @"
                SELECT COALESCE(proc_reglasprontopago2('RE', @InvoiceNumber, @ReceiptDate, 
                @NetValue, @CheckDate), 0) AS DiscountResult
                ";

                using var derivedCommand = new DB2Command(derivedQuery, connection);
                derivedCommand.Parameters.Add(new DB2Parameter("@InvoiceNumber", invoiceNumber));
                derivedCommand.Parameters.Add(new DB2Parameter("@ReceiptDate", receiptDate));
                derivedCommand.Parameters.Add(new DB2Parameter("@NetValue", netValue));
                derivedCommand.Parameters.Add(new DB2Parameter("@CheckDate", checkDate));

                var discountResult = 0m;

                using var derivedReader = await derivedCommand.ExecuteReaderAsync();
                if (await derivedReader.ReadAsync())
                {
                    discountResult = Convert.ToDecimal(derivedReader["DiscountResult"]);
                }

                return discountResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al calcular pronto pago: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return 0;
            }
        }
        public async Task InsertTransferControlAsync(WompiWebhook webhook)
        {
            if (webhook?.Data?.Transaction == null)
                throw new ArgumentNullException(nameof(webhook), "webhook o transaction nulos");

            var t = webhook.Data.Transaction;
            string reference = t.Reference ?? "";

            // 1) Limpiar cualquier sección "-ABONO-dd-mm-yyyy_hh-mm"
            //    Esto hará que, por ejemplo, "NIT-...-ABONO-14-04-2025_12-56-..." se reduzca a "NIT-...-..."
            reference = Regex.Replace(reference, @"-ABONO-\d{2}-\d{2}-\d{4}_\d{2}-\d{2}", "");

            List<string> invoiceList = new List<string>();
            string customerIdFromReference = string.Empty;

            // --------- RESTO DE TU LÓGICA ORIGINAL SIN TOCAR ---------
            if (reference.StartsWith("NIT-"))
            {
                var tokens = reference.Split('-');
                if (tokens.Length >= 4 && tokens[2] == "FAV")
                {
                    customerIdFromReference = tokens[1];
                    int numTokens = tokens.Length - 3;
                    if (numTokens % 2 == 0)
                    {
                        for (int i = 3; i < tokens.Length; i += 2)
                        {
                            string invoiceTokenCombined = tokens[i] + "-" + tokens[i + 1];
                            invoiceList.Add(invoiceTokenCombined);
                        }
                    }
                    else
                    {
                        string invoiceTokensCombined = string.Join("-", tokens.Skip(3));
                        invoiceList.Add(invoiceTokensCombined);
                    }
                }
                else
                {
                    invoiceList.Add(reference);
                }
            }
            else
            {
                invoiceList.Add(reference);
            }

            string sql = @"
        INSERT INTO control_transferencias (
            ctr_cliente_nit,
            ctr_pago_fecha,
            ctr_pago_estado,
            ctr_factura_valor,
            ctr_factura_numero,
            ctr_transaccion_numero,
            ctr_pago_franquicia,
            ctr_referencia_pago,
            ctr_usuario_nombre,
            ctr_cliente_correo,
            ctr_pago_marca,
            ctr_pago_descuento,
            ctr_registro_usuario,
            ctr_enter,
            ctr_entry_date,
            ctr_entry_time
        )
        VALUES (
            @Nit,
            @Fecha,
            @Estado,
            @Valor,
            @Factura,
            @Transaccion,
            @Franquicia,
            @ReferenciaPago,
            @Usuario,
            @Correo,
            @Marca,
            @Descuento,
            @RegistroUsuario,
            @Enter,
            @EntryDate,
            @EntryTime
        )
        ";

            DateTime fechaPago;
            if (!DateTime.TryParse(t.FinalizedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out fechaPago))
                fechaPago = DateTime.Now;

            decimal defaultValor = t.AmountInCents / 100m;
            string entryTime = DateTime.Now.ToString("HH:mm:ss");

            using var connection = new DB2Connection(_connectionString);
            await connection.OpenAsync();

            foreach (var invoiceToken in invoiceList)
            {
                string invoiceNumberStr = "";
                decimal invoiceValue = defaultValor;
                decimal discount = 0m;

                if (invoiceToken.Contains("_DP-"))
                {
                    var parts = invoiceToken.Split(new string[] { "_DP-" }, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        if (parts[0].Contains("_"))
                        {
                            var invoiceParts = parts[0].Split('_');
                            if (invoiceParts.Length == 2)
                            {
                                invoiceNumberStr = invoiceParts[0];
                                if (!decimal.TryParse(invoiceParts[1], out invoiceValue))
                                    invoiceValue = defaultValor;
                            }
                        }
                        else
                        {
                            invoiceNumberStr = parts[0];
                            invoiceValue = defaultValor;
                        }

                        string discountStr = parts[1];
                        if (discountStr.StartsWith("DP-", StringComparison.OrdinalIgnoreCase))
                        {
                            discountStr = discountStr.Substring(3);
                        }
                        if (!decimal.TryParse(discountStr, out discount))
                            discount = 0m;
                    }
                }
                else if (invoiceToken.Contains("_"))
                {
                    var parts = invoiceToken.Split('_');
                    if (parts.Length == 2)
                    {
                        invoiceNumberStr = parts[0];
                        if (!decimal.TryParse(parts[1], out invoiceValue))
                            invoiceValue = defaultValor;
                    }
                    else
                    {
                        invoiceNumberStr = invoiceToken;
                        invoiceValue = defaultValor;
                    }
                }
                else
                {
                    invoiceNumberStr = invoiceToken;
                    invoiceValue = defaultValor;
                }

                if (string.IsNullOrWhiteSpace(invoiceNumberStr))
                {
                    throw new Exception("El token de factura no contiene un número válido.");
                }

                int invoiceNumber;
                string numericPart = invoiceNumberStr;
                if (invoiceNumberStr.StartsWith("FAC", StringComparison.OrdinalIgnoreCase))
                {
                    numericPart = invoiceNumberStr.Substring(3);
                }
                if (!int.TryParse(numericPart, out invoiceNumber))
                {
                    throw new Exception($"No se pudo convertir el número de factura '{invoiceNumberStr}' a entero.");
                }

                using var cmd = new DB2Command(sql, connection);

                cmd.Parameters.Add("@Nit", DB2Type.VarChar).Value =
                    !string.IsNullOrEmpty(customerIdFromReference) ? customerIdFromReference : "NIT_QUEMADO";
                cmd.Parameters.Add("@Fecha", DB2Type.DateTime).Value = fechaPago;
                cmd.Parameters.Add("@Estado", DB2Type.VarChar).Value = t.Status ?? "";
                cmd.Parameters.Add("@Valor", DB2Type.Decimal).Value = invoiceValue;
                cmd.Parameters.Add("@Factura", DB2Type.Integer).Value = invoiceNumber;
                cmd.Parameters.Add("@Transaccion", DB2Type.VarChar).Value = t.Id ?? "";
                cmd.Parameters.Add("@Franquicia", DB2Type.VarChar).Value = t.PaymentMethod.Type ?? "";
                cmd.Parameters.Add("@ReferenciaPago", DB2Type.VarChar).Value = reference;
                cmd.Parameters.Add("@Usuario", DB2Type.VarChar).Value = t.CustomerData?.FullName ?? "";
                cmd.Parameters.Add("@Correo", DB2Type.VarChar).Value = t.CustomerEmail ?? "";
                cmd.Parameters.Add("@Marca", DB2Type.Integer).Value = "0";
                cmd.Parameters.Add("@Descuento", DB2Type.Decimal).Value = discount;
                cmd.Parameters.Add("@RegistroUsuario", DB2Type.VarChar).Value = "SYS";
                cmd.Parameters.Add("@Enter", DB2Type.VarChar).Value = "";
                cmd.Parameters.Add("@EntryDate", DB2Type.Date).Value = DateTime.Now.Date;
                cmd.Parameters.Add("@EntryTime", DB2Type.VarChar).Value = entryTime;

                await cmd.ExecuteNonQueryAsync();
            }
        }
        public async Task<List<TransferControl>> GetTransferControlsAsync(string transactionId)
        {
            var transferControls = new List<TransferControl>();

            try
            {
                using var connection = new DB2Connection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                SELECT ctr_factura_numero, ctr_factura_valor, ctr_pago_fecha, ctr_referencia_pago
                FROM control_transferencias
                WHERE ctr_transaccion_numero = @transactionId";

                using var command = new DB2Command(query, connection);
                command.Parameters.Add(new DB2Parameter("@transactionId", transactionId));

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    transferControls.Add(new TransferControl
                    {
                        FacturaNumero = reader["ctr_factura_numero"]?.ToString(),
                        FacturaValor = reader["ctr_factura_valor"] != DBNull.Value ? Convert.ToDecimal(reader["ctr_factura_valor"]) : 0,
                        PagoFecha = reader["ctr_pago_fecha"] != DBNull.Value ? Convert.ToDateTime(reader["ctr_pago_fecha"]) : DateTime.MinValue,
                        ReferenciaPago = reader["ctr_referencia_pago"]?.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al consultar transferencias: {ex.Message}");
                throw;
            }

            return transferControls;
        }
        public async Task<(string Status, object PaymentInfo, List<TransferControl> Data)> HandleConfirmationAsync(
        string transactionId,
        string wompiBaseUrl)
        {
            string rawJson;
            using (var http = new HttpClient())
            {
                var resp = await http.GetAsync($"{wompiBaseUrl}/v1/transactions/{transactionId}");
                rawJson = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException("Error al consultar Wompi.");
            }

            // 2) Parsear JSON
            using var doc = JsonDocument.Parse(rawJson);
            var dataElem = doc.RootElement.GetProperty("data");
            var txElem = dataElem.TryGetProperty("transaction", out var nested) ? nested : dataElem;

            var status = txElem.GetProperty("status").GetString() ?? "";
            var reference = txElem.GetProperty("reference").GetString() ?? "";

            var paymentMethodType = txElem.TryGetProperty("payment_method_type", out var pmt)
            ? pmt.GetString()
            : null;

            string businessAgreementCode = null;
            string paymentIntentionIdentifier = null;

            if (txElem.TryGetProperty("payment_method", out var pm) &&
                pm.TryGetProperty("extra", out var extra))
            {
                if (extra.TryGetProperty("business_agreement_code", out var bac))
                    businessAgreementCode = bac.GetString();

                if (extra.TryGetProperty("payment_intention_identifier", out var pii))
                    paymentIntentionIdentifier = pii.GetString();
            }

            var paymentInfo = new
            {
                paymentMethodType,
                businessAgreementCode,
                paymentIntentionIdentifier
            };

            // 3) Extraer lista (invoiceNumber, invoiceValue)
            reference = Regex.Replace(reference, @"-ABONO-\d{2}-\d{2}-\d{4}_\d{2}-\d{2}", "");
            var tokens = new List<string>();
            if (reference.StartsWith("NIT-") && reference.Contains("-FAV-"))
            {
                var parts = reference.Split('-');
                for (int i = 3; i + 1 < parts.Length; i += 2)
                    tokens.Add(parts[i] + "_" + parts[i + 1]);
            }
            else tokens.Add(reference);

            var invoices = tokens.Select(t =>
            {
                var segs = t.Split('_');
                var num = segs[0].StartsWith("FAC")
                           ? segs[0].Substring(3)
                           : segs[0];
                int invoiceNumber = int.Parse(num);
                decimal invoiceValue = segs.Length > 1
                    ? decimal.Parse(segs[1])
                    : txElem.GetProperty("amount_in_cents").GetInt64() / 100m;
                return (invoiceNumber, invoiceValue);
            }).ToList();

            // 4) **Idempotencia**: solo inserto si no hay aún histórico
            var already = await GetTransferenciasHistoricoAsync(transactionId);
            if (!already.Any())
            {
                await InsertTransferenciasHistoricoAsync(
                    invoices,
                    transactionId,
                    status,
                    reference
                );
            }

            if (!status.Equals("PENDING", StringComparison.OrdinalIgnoreCase))
            {
                using var conn = new DB2Connection(_connectionString);
                await conn.OpenAsync();
                const string updateSql = @"
                UPDATE control_transferencias_historico
                   SET ctr_pago_estado = @Estado
                 WHERE ctr_transaccion_numero = @Transaccion
                 AND ctr_pago_estado <> @Estado";
                using var cmd = new DB2Command(updateSql, conn);
                cmd.Parameters.Add(new DB2Parameter("@Estado", DB2Type.VarChar) { Value = status });
                cmd.Parameters.Add(new DB2Parameter("@Transaccion", DB2Type.VarChar) { Value = transactionId });
                await cmd.ExecuteNonQueryAsync();
            }

            var data = status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)
                ? await GetTransferenciasHistoricoAsync(transactionId)
                : await GetTransferControlsAsync(transactionId);

            return (status, paymentInfo, data);
        }
        public async Task InsertTransferenciasHistoricoAsync(
        List<(int invoiceNumber, decimal invoiceValue)> invoices,
        string transactionId,
        string status,
        string rawReference)
        {
            const string sql = @"
            INSERT INTO control_transferencias_historico (
                ctr_transaccion_numero,
                ctr_factura_numero,
                ctr_factura_valor,
                ctr_pago_estado,
                ctr_pago_fecha,
                ctr_referencia_pago,
                ctr_cliente_ip
            ) VALUES (
                @Transaccion,
                @Factura,
                @Valor,
                @Estado,
                @Fecha,
                @ReferenciaPago,
                @ClienteIp
            )";

            var ip = _httpContextAccessor.HttpContext?
                         .Connection?
                         .RemoteIpAddress?
                         .ToString();

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            foreach (var (invoiceNumber, invoiceValue) in invoices)
            {
                using var cmd = new DB2Command(sql, conn);
                cmd.Parameters.Add(new DB2Parameter("@Transaccion", DB2Type.VarChar) { Value = transactionId });
                cmd.Parameters.Add(new DB2Parameter("@Factura", DB2Type.Integer) { Value = invoiceNumber });
                cmd.Parameters.Add(new DB2Parameter("@Valor", DB2Type.Decimal) { Value = invoiceValue });
                cmd.Parameters.Add(new DB2Parameter("@Estado", DB2Type.VarChar) { Value = status });
                cmd.Parameters.Add(new DB2Parameter("@Fecha", DB2Type.DateTime) { Value = DateTime.Now });
                cmd.Parameters.Add(new DB2Parameter("@ReferenciaPago", DB2Type.VarChar) { Value = rawReference });
                cmd.Parameters.Add(new DB2Parameter("@ClienteIp", DB2Type.VarChar) { Value = (object)ip ?? DBNull.Value });

                await cmd.ExecuteNonQueryAsync();
            }
        }
        public async Task<List<TransferControl>> GetTransferenciasHistoricoAsync(string transactionId)
        {
            var list = new List<TransferControl>();

            using var conn = new DB2Connection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
            SELECT 
                ctr_factura_numero, 
                ctr_factura_valor, 
                ctr_pago_fecha,
                ctr_referencia_pago
              FROM control_transferencias_historico
             WHERE ctr_transaccion_numero = @transactionId";

            using var cmd = new DB2Command(sql, conn);
            cmd.Parameters.Add(new DB2Parameter("@transactionId", DB2Type.VarChar) { Value = transactionId });

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TransferControl
                {
                    FacturaNumero = reader["ctr_factura_numero"]?.ToString(),
                    FacturaValor = reader["ctr_factura_valor"] != DBNull.Value
                                        ? Convert.ToDecimal(reader["ctr_factura_valor"])
                                        : 0,
                    PagoFecha = reader["ctr_pago_fecha"] != DBNull.Value
                                        ? Convert.ToDateTime(reader["ctr_pago_fecha"])
                                        : DateTime.MinValue,
                    ReferenciaPago = reader["ctr_referencia_pago"]?.ToString()

                });
            }

            return list;
        }
    }
}

