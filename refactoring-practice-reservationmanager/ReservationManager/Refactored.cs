using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

public static class Constants
{
    public const int CacheExpirationMinutes = 30;
}

public static class DiscountCodeFactory
{
    public static decimal GetDiscountPercentage(string discountCode)
    {
        return discountCode switch
        {
            "SUMMER10" => 0.10m,
            "WINTER20" => 0.20m,
            "SPRING15" => 0.15m,
            _ => 0m
        };
    }
}

public static class MembershipLevelFactory
{
    public static decimal GetDiscountPercentage(string membershipLevel)
    {
        return membershipLevel switch
        {
            "gold" => 0.05m,
            "platinum" => 0.10m,
            _ => 0m
        };
    }
}

public class Customer
{
    public string Name { get; set; }
    public string MembershipLevel { get; set; }
    public decimal Balance { get; set; }
}

public interface ICustomerRepository
{
    Customer GetCustomerById(int customerId);
    void UpdateBalance(int customerId, decimal newBalance);
}

public class CustomerRepository : ICustomerRepository
{
    private readonly string _connectionString;

    public CustomerRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public Customer GetCustomerById(int customerId)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Open();

        using var cmd = new SqlCommand("SELECT * FROM customers WHERE id = @customerId", conn);
        cmd.Parameters.AddWithValue("@customerId", customerId);

        using var reader = cmd.ExecuteReader();

        if (reader.Read())
        {
            return new Customer
            {
                Name = reader["name"].ToString(),
                MembershipLevel = reader["membership_level"].ToString(),
                Balance = (decimal)reader["balance"]
            };
        }

        return null;
    }

    public void UpdateBalance(int customerId, decimal newBalance)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Open();
        using var cmd = new SqlCommand("UPDATE customers SET balance = @newBalance WHERE id = @customerId", conn);
        cmd.Parameters.AddWithValue("@newBalance", newBalance);
        cmd.Parameters.AddWithValue("@customerId", customerId);
        cmd.ExecuteNonQuery();
    }
}

public class Room
{
    public bool IsAvailable { get; set; }
    public decimal PricePerNight { get; set; }
    public string RoomNumber { get; set; }
}

public interface IRoomRepository
{
    Room GetRoomById(int roomId);
    void UpdateRoomAvailability(int roomId, bool isAvailable);
}

public class RoomRepository : IRoomRepository
{
    private readonly string _connectionString;

    public RoomRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public Room GetRoomById(int roomId)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Open();

        using var cmd = new SqlCommand("SELECT * FROM rooms WHERE id = @roomId", conn);
        cmd.Parameters.AddWithValue("@roomId", roomId);

        using var reader = cmd.ExecuteReader();

        if (reader.Read())
        {
            return new Room
            {
                IsAvailable = (bool)reader["is_available"],
                PricePerNight = (decimal)reader["price_per_night"],
                RoomNumber = reader["room_number"].ToString()
            };
        }

        return null;
    }

    public void UpdateRoomAvailability(int roomId, bool isAvailable)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Open();
        using var cmd = new SqlCommand("UPDATE rooms SET is_available = @isAvailable WHERE id = @roomId", conn);
        cmd.Parameters.AddWithValue("@isAvailable", isAvailable);
        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.ExecuteNonQuery();
    }
}

public interface IReservationRepository
{
    void CreateReservation(int customerId, int roomId, string roomType, int nights, decimal total, string discountCode, string membershipLevel);
}

public class ReservationRepository : IReservationRepository
{
    private readonly string _connectionString;

    public ReservationRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void CreateReservation(int customerId, int roomId, string roomType, int nights, decimal total, string discountCode, string membershipLevel)
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Open();
        using var cmd = new SqlCommand("INSERT INTO reservations (customer_id, room_id, room_type, nights, total, discount_code, membership_level, created_at) VALUES (@customerId, @roomId, @roomType, @nights, @total, @discountCode, @membershipLevel, @createdAt)", conn);
        cmd.Parameters.AddWithValue("@customerId", customerId);
        cmd.Parameters.AddWithValue("@roomId", roomId);
        cmd.Parameters.AddWithValue("@roomType", roomType);
        cmd.Parameters.AddWithValue("@nights", nights);
        cmd.Parameters.AddWithValue("@total", total);
        cmd.Parameters.AddWithValue("@discountCode", discountCode);
        cmd.Parameters.AddWithValue("@membershipLevel", membershipLevel);
        cmd.Parameters.AddWithValue("@createdAt", DateTime.Now);
        cmd.ExecuteNonQuery();
    }
}

public interface IReservationManager
{
    string CreateReservation(int customerId, int roomId, string roomType, int nights, string discountCode);
}

public class ReservationManager : IReservationManager
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IRoomRepository _roomRepository;
    private readonly IReservationRepository _reservationRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger _logger;

    public ReservationManager(ICustomerRepository customerRepository, IRoomRepository roomRepository, IReservationRepository reservationRepository, IMemoryCache cache, ILogger logger)
    {
        _customerRepository = customerRepository;
        _roomRepository = roomRepository;
        _reservationRepository = reservationRepository;
        _cache = cache;
        _logger = logger;
    }

    private decimal CalculateTotal(Room room, int nights, string discountCode, Customer customer)
    {
        decimal total = room.PricePerNight * nights;

        decimal discountPercentage = DiscountCodeFactory.GetDiscountPercentage(discountCode);
        total -= total * discountPercentage;

        decimal membershipDiscount = MembershipLevelFactory.GetDiscountPercentage(customer.MembershipLevel);
        total -= total * membershipDiscount;

        return total;
    }

    public string CreateReservation(int customerId, int roomId, string roomType, int nights, string discountCode)
    {
        string msg = $"Creating reservation for customer ID {customerId} room ID {roomId} nights {nights} discount {discountCode}";
        var cacheKey = $"customer_{customerId}";
        if (!_cache.TryGetValue(cacheKey, out Customer customer))
        {
            customer = _customerRepository.GetCustomerById(customerId);

            if (customer == null)
            {
                msg = $"Customer with ID {customerId} not found.";
                _logger.LogWarning(msg);
                return msg;
            }
            _cache.Set(cacheKey, customer, TimeSpan.FromMinutes(Constants.CacheExpirationMinutes));
        }

        cacheKey = $"room_{roomId}";
        if (!_cache.TryGetValue(cacheKey, out Room room))
        {
            room = _roomRepository.GetRoomById(roomId);
            if (room == null)
            {
                msg = $"Room with ID {roomId} not found.";
                _logger.LogWarning(msg);
                return msg;
            }
            _cache.Set(cacheKey, room, TimeSpan.FromMinutes(Constants.CacheExpirationMinutes));
        }

        if (!room.IsAvailable)
        {
            msg = $"Room {room.RoomNumber} is not available.";
            _logger.LogInformation(msg);
            return msg;
        }

        decimal total = CalculateTotal(room, nights, discountCode, customer);

        if (customer.Balance < total)
        {
            msg = $"Customer {customer.Name} has insufficient balance. Required: {total}, Available: {customer.Balance}";
            _logger.LogInformation(msg);
            return msg;
        }

        try
        {
            _roomRepository.UpdateRoomAvailability(roomId, false);
            _customerRepository.UpdateBalance(customerId, customer.Balance - total);
            _reservationRepository.CreateReservation(customerId, roomId, roomType, nights, total, discountCode, customer.MembershipLevel);
        }
        catch (Exception ex)
        {
            msg = $"Error creating reservation: {ex.Message}";
            _logger.LogError(ex, msg);
            return msg;
        }

        msg = $"Reservation created for customer {customer.Name} room {room.RoomNumber} nights {nights} total {total}";
        _logger.LogInformation(msg);
        return msg;
    }
}