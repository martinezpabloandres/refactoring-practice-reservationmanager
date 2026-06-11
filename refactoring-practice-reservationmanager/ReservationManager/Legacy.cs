using Microsoft.Data.SqlClient;

public class ReservationManagerLegacy
{
    public string CreateReservation(int customerId, int roomId, string roomType, int nights, string discountCode)
    {
        SqlConnection conn = new SqlConnection("Server=myserver;Database=hotel;User Id=admin;Password=secret123;");
        conn.Open();

        SqlCommand customerCmd = new SqlCommand("SELECT * FROM customers WHERE id = " + customerId, conn);
        SqlDataReader customerReader = customerCmd.ExecuteReader();

        if (!customerReader.Read())
        {
            conn.Close();
            return "Customer not found";
        }

        string customerName = customerReader["name"].ToString();
        string membershipLevel = customerReader["membership_level"].ToString();
        double balance = (double)customerReader["balance"];
        customerReader.Close();

        SqlCommand roomCmd = new SqlCommand("SELECT * FROM rooms WHERE id = " + roomId, conn);
        SqlDataReader roomReader = roomCmd.ExecuteReader();

        if (!roomReader.Read())
        {
            conn.Close();
            return "Room not found";
        }

        bool isAvailable = (bool)roomReader["is_available"];
        double pricePerNight = (double)roomReader["price_per_night"];
        string roomNumber = roomReader["room_number"].ToString();
        roomReader.Close();

        if (!isAvailable)
        {
            conn.Close();
            return "Room is not available";
        }

        double total = pricePerNight * nights;

        if (discountCode == "SUMMER10")
        {
            total = total * 0.90;
        }
        else if (discountCode == "WINTER20")
        {
            total = total * 0.80;
        }
        else if (discountCode == "SPRING15")
        {
            total = total * 0.85;
        }

        if (membershipLevel == "gold")
        {
            total = total * 0.95;
        }
        else if (membershipLevel == "platinum")
        {
            total = total * 0.90;
        }

        if (balance < total)
        {
            conn.Close();
            return "Insufficient balance";
        }

        SqlCommand updateRoom = new SqlCommand("UPDATE rooms SET is_available = 0 WHERE id = " + roomId, conn);
        updateRoom.ExecuteNonQuery();

        SqlCommand updateBalance = new SqlCommand("UPDATE customers SET balance = " + (balance - total) + " WHERE id = " + customerId, conn);
        updateBalance.ExecuteNonQuery();

        SqlCommand insertReservation = new SqlCommand("INSERT INTO reservations (customer_id, room_id, room_type, nights, total, discount_code, membership_level, created_at) VALUES (" + customerId + ", " + roomId + ", '" + roomType + "', " + nights + ", " + total + ", '" + discountCode + "', '" + membershipLevel + "', '" + DateTime.Now + "')", conn);
        insertReservation.ExecuteNonQuery();

        Console.WriteLine("Reservation created for customer " + customerName + " room " + roomNumber + " nights " + nights + " total " + total);

        conn.Close();
        return "Reservation created successfully. Total: " + total;
    }
}