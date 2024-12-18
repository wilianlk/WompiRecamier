using IBM.Data.Db2;

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

                // Query para validar cm_resal[2,15]
                string query = @"
                    SELECT COUNT(1)
                    FROM custmain c
                    WHERE SUBSTR(c.cm_resal, 2, 15) = @resal";

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

        public async Task<List<object>> GetPendingPaymentsAsync(string company, string customer)
        {
            var payments = new List<object>();

            try
            {
                using var connection = new DB2Connection(_connectionString);
                await connection.OpenAsync();

                string query = @"
                SELECT in_num, 
                        (xin_mont - xin_paga) AS valor_neto,
                        llamar_proc_reglasprontopago(in_cmpy, in_num, '12/17/2024', (xin_mont - xin_paga), 0, '12/17/2024', 'V') AS resultado_descuento,
                        (xin_mont - xin_paga) - llamar_proc_reglasprontopago(in_cmpy, in_num, '12/17/2024', (xin_mont - xin_paga), 0, '12/17/2024', 'V') AS valor_neto_con_descuento
                FROM invhead, xinvhead
                WHERE in_cmpy = @company
                    AND SUBSTR(in_cust, 2, 15) = @customer
                    AND in_amt > in_paid
                    AND in_post != 'V'
                    AND xin_num = in_num
                    AND xin_cmpy = in_cmpy
                    AND xin_mont > xin_paga
                ORDER BY in_num, in_date";

                using var command = new DB2Command(query, connection);
                command.Parameters.Add(new DB2Parameter("@company", company));
                command.Parameters.Add(new DB2Parameter("@customer", customer));

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    payments.Add(new
                    {
                        InvoiceNumber = reader["in_num"].ToString(),
                        NetValue = reader["valor_neto"].ToString(),
                        DiscountResult = reader["resultado_descuento"].ToString(),
                        NetValueWithDiscount = reader["valor_neto_con_descuento"].ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener pagos pendientes: {ex.Message}");
            }

            return payments;
        }


    }
}
