using IBM.Data.Db2;
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

        public async Task<bool> ValidateCustomerAsync(string resal)
        {
            try
            {
                using var connection = new DB2Connection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                    SELECT COUNT(1)
                    FROM custmain c
                    WHERE cm_cmpy = 'RE'
                    AND SUBSTR(c.cm_resal, 2, 15) = @resal
                    OR  SUBSTR(c.cm_cust, 2, 15) = @resal";

                using var command = new DB2Command(query, connection);
                command.Parameters.Add(new DB2Parameter("@resal", resal));

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al ejecutar la consulta: {ex.Message}");
                return false;
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
                SELECT SUBSTR(cm_cust, 2, 15) AS cm_cust
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
                  AND SUBSTR(in_cust, 2, 15) = @CustomerCode
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

                    // Segunda consulta para calcular el descuento
                    string derivedQuery = @"
                    SELECT COALESCE(llamar_proc_reglasprontopago('RE', @InvoiceNumber, '2024-12-17', @NetValue, 0, '12/17/2024', 'V'), 0) AS DiscountResult
                    ";

                    using var derivedCommand = new DB2Command(derivedQuery, connection);
                    derivedCommand.Parameters.Add(new DB2Parameter("@InvoiceNumber", invoiceNumber));
                    derivedCommand.Parameters.Add(new DB2Parameter("@NetValue", netValue));

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
    }
}
