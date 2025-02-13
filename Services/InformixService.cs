using IBM.Data.Db2;
using System.Globalization;
using WompiRecamier.Models;

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

                string validateQuery = @"
                    SELECT COUNT(1)
                    FROM custmain c
                    WHERE cm_cmpy = 'RE'
                    AND SUBSTR(c.cm_resal, 2, 15) = @resal
                    OR  SUBSTR(c.cm_cust, 2, 15) = @resal";

                using var validateCommand = new DB2Command(validateQuery, connection);
                validateCommand.Parameters.Add(new DB2Parameter("@resal", resal));

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

                string query = @"
                SELECT TRIM(cm_name) AS cm_name, TRIM(cm_tele) AS cm_tele
                FROM custmain c
                WHERE c.cm_cmpy = 'RE'
                  AND (SUBSTR(c.cm_cust, 2, 15) = @resal OR SUBSTR(c.cm_resal, 2, 15) = @resal)
                LIMIT 1
                ";

                using var command = new DB2Command(query, connection);
                command.Parameters.Add(new DB2Parameter("@resal", resal));

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
                string getCustomerCodeQuery = @"
                SELECT cm_cust
                FROM custmain c
                WHERE c.cm_cmpy = 'RE'
                  AND (SUBSTR(c.cm_cust, 2, 15) = @resal 
                OR SUBSTR(c.cm_resal, 2, 15) = @resal)
                ";

                using var getCustomerCodeCommand = new DB2Command(getCustomerCodeQuery, connection);
                getCustomerCodeCommand.Parameters.Add(new DB2Parameter("@resal", resal));

                string customerCode = null;
                using (var reader = await getCustomerCodeCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        customerCode = reader["cm_cust"].ToString();
                    }
                }

                // Si no se encuentra el código de cliente, retorna una lista vacía
                if (string.IsNullOrEmpty(customerCode))
                {
                    Console.WriteLine("No se encontró un cliente para el código proporcionado.");
                    return new List<PaymentInfo>();
                }

                // Paso 2: Consulta principal para obtener detalles de las facturas
                string baseQuery = @"
                SELECT in_num, 
                       (xin_mont - xin_paga) AS valor_neto,
                       in_date
                FROM invhead
                JOIN xinvhead ON xin_num = in_num AND xin_cmpy = in_cmpy
                WHERE in_cmpy = 'RE'
                  AND in_cust = @CustomerCode
                  AND in_amt > in_paid
                  AND in_post != 'V'
                  AND xin_mont > xin_paga
                ORDER BY in_num, in_date
                ";

                using var baseCommand = new DB2Command(baseQuery, connection);
                baseCommand.Parameters.Add(new DB2Parameter("@CustomerCode", customerCode));
                baseCommand.CommandTimeout = 300;

                var paymentInfoList = new List<PaymentInfo>();

                using var baseReader = await baseCommand.ExecuteReaderAsync();
                while (await baseReader.ReadAsync())
                {
                    // Lee los valores de la consulta base
                    var invoiceNumber = baseReader["in_num"].ToString();
                    var netValue = Convert.ToDecimal(baseReader["valor_neto"]);
                    var invoiceDate = Convert.ToDateTime(baseReader["in_date"]);
                    var currentDate = DateTime.Now;
                    var receiptDate = currentDate.ToString("yyyy-MM-dd");
                    var checkDate = currentDate.ToString("MM/dd/yyyy");

                    // Segunda consulta para calcular el descuento
                    string derivedQuery = @"
                    SELECT COALESCE(proc_reglasprontopago('RE', @InvoiceNumber, @ReceiptDate, @NetValue, 0, @CheckDate, 'V'), 0) AS DiscountResult
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
                        InvoiceDate = invoiceDate
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
        public async Task InsertTransferControlAsync(WompiWebhook webhook)
        {
            if (webhook?.Data?.Transaction == null)
                throw new ArgumentNullException(nameof(webhook), "webhook o transaction nulos");

            var t = webhook.Data.Transaction;
            string reference = t.Reference ?? "";

            List<string> invoiceList = new List<string>();
            string customerIdFromReference = string.Empty;

            if (reference.StartsWith("NIT-"))
            {

                var tokens = reference.Split('-');

                if (tokens.Length >= 4 && tokens[2] == "FAV")
                {
                    customerIdFromReference = tokens[1];
                    invoiceList.AddRange(tokens.Skip(3));
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

            decimal defaultValor = t.AmountInCents / 100m;
            string entryTime = DateTime.Now.ToString("HH:mm:ss");

            using var connection = new DB2Connection(_connectionString);
            await connection.OpenAsync();

            foreach (var invoiceToken in invoiceList)
            { 

                string invoiceNumber = invoiceToken;
                decimal invoiceValue = defaultValor;

                if (invoiceToken.Contains("_"))
                {
                    var parts = invoiceToken.Split('_');
                    if (parts.Length == 2)
                    {
                        invoiceNumber = parts[0];
                        if (decimal.TryParse(parts[1], out decimal parsedValue))
                        {
                            invoiceValue = parsedValue;
                        }
                    }
                }

                using var cmd = new DB2Command(sql, connection);

                cmd.Parameters.Add("@Nit", DB2Type.VarChar).Value =
                    !string.IsNullOrEmpty(customerIdFromReference) ? customerIdFromReference : "NIT_QUEMADO";
                cmd.Parameters.Add("@Fecha", DB2Type.DateTime).Value = fechaPago;
                cmd.Parameters.Add("@Estado", DB2Type.VarChar).Value = t.Status ?? "";
                cmd.Parameters.Add("@Valor", DB2Type.Decimal).Value = invoiceValue;
                cmd.Parameters.Add("@Factura", DB2Type.VarChar).Value = invoiceNumber;
                cmd.Parameters.Add("@Transaccion", DB2Type.VarChar).Value = t.Id ?? "";
                cmd.Parameters.Add("@Franquicia", DB2Type.VarChar).Value = t.PaymentMethodType ?? "";
                cmd.Parameters.Add("@Usuario", DB2Type.VarChar).Value = t.CustomerData?.FullName ?? "";
                cmd.Parameters.Add("@Correo", DB2Type.VarChar).Value = t.CustomerEmail ?? "";
                cmd.Parameters.Add("@Marca", DB2Type.VarChar).Value = t.PaymentMethod?.Type ?? "";
                cmd.Parameters.Add("@Descuento", DB2Type.Decimal).Value = 0m;
                cmd.Parameters.Add("@RegistroUsuario", DB2Type.VarChar).Value = "SYS";
                cmd.Parameters.Add("@Enter", DB2Type.VarChar).Value = "";
                cmd.Parameters.Add("@EntryDate", DB2Type.Date).Value = DateTime.Now.Date;
                cmd.Parameters.Add("@EntryTime", DB2Type.VarChar).Value = entryTime;

                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}

