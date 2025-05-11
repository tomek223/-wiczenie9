using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using Microsoft.Data.SqlClient;
using Tutorial9.Model;

namespace Tutorial9.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WarehouseController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public WarehouseController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost]
        public IActionResult AddProductToWarehouse([FromBody] WarehouseRequest request)
        {
            if (request.Amount <= 0)
                return BadRequest("Amount must be greater than 0");

            using var connection = new SqlConnection(_configuration.GetConnectionString("Default"));
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                var cmd = new SqlCommand("SELECT 1 FROM Product WHERE IdProduct = @IdProduct", connection, transaction);
                cmd.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                if (cmd.ExecuteScalar() is null)
                    return NotFound("Product not found");

                cmd = new SqlCommand("SELECT 1 FROM Warehouse WHERE IdWarehouse = @IdWarehouse", connection, transaction);
                cmd.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                if (cmd.ExecuteScalar() is null)
                    return NotFound("Warehouse not found");

                cmd = new SqlCommand(@"
                    SELECT IdOrder FROM [Order]
                    WHERE IdProduct = @IdProduct AND Amount = @Amount AND CreatedAt < @CreatedAt", connection, transaction);
                cmd.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                cmd.Parameters.AddWithValue("@Amount", request.Amount);
                cmd.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);
                var orderIdObj = cmd.ExecuteScalar();
                if (orderIdObj is null)
                    return NotFound("Matching order not found");

                int orderId = (int)orderIdObj;

                cmd = new SqlCommand("SELECT 1 FROM Product_Warehouse WHERE IdOrder = @IdOrder", connection, transaction);
                cmd.Parameters.AddWithValue("@IdOrder", orderId);
                if (cmd.ExecuteScalar() is not null)
                    return Conflict("Order already fulfilled");

                cmd = new SqlCommand("UPDATE [Order] SET FulfilledAt = GETDATE() WHERE IdOrder = @IdOrder", connection, transaction);
                cmd.Parameters.AddWithValue("@IdOrder", orderId);
                cmd.ExecuteNonQuery();

                cmd = new SqlCommand("SELECT Price FROM Product WHERE IdProduct = @IdProduct", connection, transaction);
                cmd.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                decimal price = (decimal)cmd.ExecuteScalar();

                cmd = new SqlCommand(@"
                    INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt)
                    VALUES (@IdWarehouse, @IdProduct, @IdOrder, @Amount, @Price, GETDATE());
                    SELECT SCOPE_IDENTITY();", connection, transaction);
                cmd.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
                cmd.Parameters.AddWithValue("@IdProduct", request.IdProduct);
                cmd.Parameters.AddWithValue("@IdOrder", orderId);
                cmd.Parameters.AddWithValue("@Amount", request.Amount);
                cmd.Parameters.AddWithValue("@Price", price * request.Amount);

                int insertedId = Convert.ToInt32(cmd.ExecuteScalar());

                transaction.Commit();
                return Ok(insertedId);
            }
            catch
            {
                transaction.Rollback();
                return StatusCode(500, "Unexpected error occurred");
            }
        }

        [HttpPost("procedure")]
        public IActionResult AddProductToWarehouseUsingProcedure([FromBody] WarehouseRequest request)
        {
            using var connection = new SqlConnection(_configuration.GetConnectionString("Default"));
            connection.Open();

            var cmd = new SqlCommand("AddProductToWarehouse", connection);
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@IdProduct", request.IdProduct);
            cmd.Parameters.AddWithValue("@IdWarehouse", request.IdWarehouse);
            cmd.Parameters.AddWithValue("@Amount", request.Amount);
            cmd.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

            try
            {
                var result = cmd.ExecuteScalar();
                return Ok(result);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }
    }
}
