using IBM.Data.Db2;
using System.Data;
using System.Globalization;
using WompiRecamier.Models;
using System.Transactions;

namespace WompiRecamier.Services
{
    public class InformixService
    {
        private readonly string _connectionString;

        public InformixService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("InformixConnection");
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
                    OR SUBSTR(cwe_cust, 2, 15) = @resal)";

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

                // Paso 1: Consulta para obtener el valor de cm_cust
                /*string getCustomerCodeQuery = @"
                SELECT cm_cust
                FROM custmain c
                WHERE c.cm_cmpy = 'RE'
                  AND (SUBSTR(c.cm_cust, 2, 15) = @resal 
                OR SUBSTR(c.cm_resal, 2, 15) = @resal)
                ";
                using var getCustomerCodeCommand = new DB2Command(getCustomerCodeQuery, connection);
                getCustomerCodeCommand.Parameters.Add(new DB2Parameter("@resal", resal));*/

                using var getCustomerCodeCommand = new DB2Command("proc_validate_customer_count_wompy", connection);
                getCustomerCodeCommand.CommandType = CommandType.StoredProcedure;
                getCustomerCodeCommand.Parameters.Add(new DB2Parameter("p_resal", resal));
                getCustomerCodeCommand.Parameters.Add(new DB2Parameter("p_query_type", "U"));

                var result = await getCustomerCodeCommand.ExecuteScalarAsync();
                string customerCode = result?.ToString();

                /*string customerCode = null;
                using (var reader = await getCustomerCodeCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        customerCode = reader["cm_cust"].ToString();
                    }
                }*/

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
                    var netValue = Convert.ToDecimal(baseReader["valor_neto"]);
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
                    derivedCommand.Parameters.Add(new DB2Parameter("@CheckDate", checkDate));


                    var discountResult = 0m;
                    using var derivedReader = await derivedCommand.ExecuteReaderAsync();
                    if (await derivedReader.ReadAsync())
                    {
                        discountResult = Convert.ToDecimal(derivedReader["DiscountResult"]);
                    }

                    // Agregar los resultados al objeto final
                    paymentInfoList.Add(new PaymentInfo
                    {
                        InvoiceNumber = invoiceNumber,
                        NetValue = netValue,
                        DiscountResult = discountResult,
                        NetValueWithDiscount = netValue - discountResult,
                        InvoiceDate = invoiceDate,
                        MiscInfo = miscInfo
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
                SELECT COALESCE(proc_reglasprontopago('RE', @InvoiceNumber, @ReceiptDate, 
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

            List<string> invoiceList = new List<string>();
            string customerIdFromReference = string.Empty;

            // Se asume que el reference tiene el formato:
            // NIT-<customerId>-FAV-<invoiceInfo1>-<discount1>-<invoiceInfo2>-<discount2>-...
            // Por ejemplo: 
            // NIT-9003300530-FAV-2930532_3671974_DP-0-2932634_477806_DP-0-2941706_1092449_DP-0
            if (reference.StartsWith("NIT-"))
            {
                var tokens = reference.Split('-');
                if (tokens.Length >= 4 && tokens[2] == "FAV")
                {
                    customerIdFromReference = tokens[1];
                    // Se asume que los tokens restantes vienen en pares (invoiceInfo y discount)
                    int numTokens = tokens.Length - 3;
                    if (numTokens % 2 == 0)
                    {
                        // Por cada par se reconstruye el token completo de factura
                        for (int i = 3; i < tokens.Length; i += 2)
                        {
                            // Ejemplo: tokens[i]="2930532_3671974" y tokens[i+1]="DP-0"
                            string invoiceTokenCombined = tokens[i] + "-" + tokens[i + 1];
                            invoiceList.Add(invoiceTokenCombined);
                        }
                    }
                    else
                    {
                        // Si no vienen en pares, se unen todos los tokens a partir del índice 3
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

            // Valor por defecto basado en t.AmountInCents
            decimal defaultValor = t.AmountInCents / 100m;
            string entryTime = DateTime.Now.ToString("HH:mm:ss");

            using var connection = new DB2Connection(_connectionString);
            await connection.OpenAsync();

            foreach (var invoiceToken in invoiceList)
            {
                // Variables para extraer la información
                string invoiceNumberStr = "";
                decimal invoiceValue = defaultValor;
                decimal discount = 0m; // Descuento para la factura

                // Se espera que cada token tenga el formato "2930532_3671974-DP-0" o "FAC001_461808-DP-19404"
                if (invoiceToken.Contains("_DP-"))
                {
                    // Importante: se usa "_DP-" como separador (no "-DP_")
                    var parts = invoiceToken.Split(new string[] { "_DP-" }, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        // La parte izquierda puede tener el formato "2930532_3671974" o "FAC001_461808"
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
                        // La parte derecha es el descuento. Si llega con el prefijo "DP-", lo eliminamos.
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
                    // Formato sin descuento: por ejemplo, "2930532_3671974"
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

                // Convertir invoiceNumberStr a entero.
                // Si existe un prefijo "FAC", se remueve.
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
                cmd.Parameters.Add("@Franquicia", DB2Type.VarChar).Value = t.PaymentMethodType ?? "";
                cmd.Parameters.Add("@Usuario", DB2Type.VarChar).Value = t.CustomerData?.FullName ?? "";
                cmd.Parameters.Add("@Correo", DB2Type.VarChar).Value = t.CustomerEmail ?? "";
                cmd.Parameters.Add("@Marca", DB2Type.VarChar).Value = "";
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
                SELECT ctr_factura_numero, ctr_factura_valor, ctr_pago_fecha
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
                        PagoFecha = reader["ctr_pago_fecha"] != DBNull.Value ? Convert.ToDateTime(reader["ctr_pago_fecha"]) : DateTime.MinValue
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

    }
}

