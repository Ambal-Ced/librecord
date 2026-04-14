using Microsoft.Extensions.Options;

namespace LibRecord.Services;

public sealed class AdminOptions
{
  public string Password { get; set; } = "BSU123";
  public int MaxCustomFields { get; set; } = 6;
  public string AuthCookieName { get; set; } = "librecord_admin";
}

public static class AdminAuth
{
  public static bool IsAdmin(HttpRequest request, AdminOptions options)
  {
    return request.Cookies.TryGetValue(options.AuthCookieName, out var v) && v == "1";
  }

  public static void SignIn(HttpResponse response, AdminOptions options)
  {
    response.Cookies.Append(
      options.AuthCookieName,
      "1",
      new CookieOptions
      {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = false,
        IsEssential = true,
        Expires = DateTimeOffset.UtcNow.AddHours(8),
      }
    );
  }

  public static void SignOut(HttpResponse response, AdminOptions options)
  {
    response.Cookies.Delete(options.AuthCookieName);
  }
}

