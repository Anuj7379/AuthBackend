using AuthApi.Data;
using AuthApi.DTOs;
using AuthApi.Models;
using AuthApi.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly JwtService _jwtService;
    private readonly bool _isProduction;

    public AuthController(
        AppDbContext context,
        JwtService jwtService,
        IWebHostEnvironment environment)
    {
        _context = context;
        _jwtService = jwtService;
        _isProduction = environment.IsProduction();
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterDto dto)
    {
        var email = NormalizeEmail(dto.Email);

        if (await _context.Users
            .AnyAsync(x => x.Email == email))
        {
            return BadRequest(
                new
                {
                    message = "Email already exists"
                });
        }

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = email,
            PasswordHash =
                BCrypt.Net.BCrypt.HashPassword(
                    dto.Password)
        };

        _context.Users.Add(user);

        await _context.SaveChangesAsync();

        return Ok(
            new
            {
                message = "User Registered"
            });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginDto dto)
    {
        var email = NormalizeEmail(dto.Email);

        var user =
            await _context.Users
            .FirstOrDefaultAsync(
                x => x.Email == email);

        if (user == null)
        {
            return Unauthorized(
                new
                {
                    message = "Invalid Credentials"
                });
        }

        bool verified =
            BCrypt.Net.BCrypt.Verify(
                dto.Password,
                user.PasswordHash);

        if (!verified)
        {
            return Unauthorized(
                new
                {
                    message = "Invalid Credentials"
                });
        }

        var token =
            _jwtService.GenerateToken(user);

        Response.Cookies.Append(
            "jwt",
            token,
            CreateAuthCookieOptions());

        return Ok(
            new
            {
                message = "Login Success"
            });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(
            "jwt",
            CreateAuthCookieOptions());

        return Ok(
            new
            {
                message = "Logout Success"
            });
    }

    private CookieOptions CreateAuthCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = _isProduction,
            SameSite = _isProduction ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddHours(1)
        };
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }
}
