namespace HelpdeskApi.Services;

/// <summary>Lỗi nghiệp vụ: middleware trong Program.cs sẽ đổi thành HTTP status tương ứng.</summary>
public class AppException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
