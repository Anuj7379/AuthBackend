using AuthApi.Data;
using AuthApi.DTOs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using System.Security.Claims;

namespace AuthApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly AppDbContext _context;

    public UserController(
        AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(
                new
                {
                    message = "Invalid user session"
                });

        var user =
            await _context.Users.FindAsync(
                userId);

        if (user == null)
            return NotFound();

        return Ok(ToProfileResponse(user));
    }

    [HttpPost("profile/setup")]
    public Task<IActionResult> SetupProfile(
        UpdateProfileDto dto)
    {
        return UpdateProfile(dto);
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        UpdateProfileDto dto)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(
                new
                {
                    message = "Invalid user session"
                });

        var user =
            await _context.Users.FindAsync(
                userId);

        if (user == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Email)
            && !dto.Email.Equals(
                user.Email,
                StringComparison.OrdinalIgnoreCase))
        {
            var email = NormalizeEmail(dto.Email);
            var emailExists =
                await _context.Users.AnyAsync(x =>
                    x.Email == email
                    && x.Id != user.Id);

            if (emailExists)
            {
                return BadRequest(
                    new
                    {
                        message = "Email already exists"
                    });
            }

            user.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(dto.Name))
            user.Name = dto.Name.Trim();

        if (dto.PhoneNumber is not null)
            user.PhoneNumber = NormalizeOptional(dto.PhoneNumber);

        if (dto.Bio is not null)
            user.Bio = NormalizeOptional(dto.Bio);

        if (dto.Address is not null)
            user.Address = NormalizeOptional(dto.Address);

        if (dto.DateOfBirth.HasValue)
            user.DateOfBirth = dto.DateOfBirth;

        if (dto.ProfileImageUrl is not null)
            user.ProfileImageUrl = NormalizeOptional(dto.ProfileImageUrl);

        await _context.SaveChangesAsync();

        return Ok(
            new
            {
                message = "Profile Updated",
                profile = ToProfileResponse(user)
            });
    }

    [HttpDelete("profile")]
    public async Task<IActionResult> DeleteProfile()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(
                new
                {
                    message = "Invalid user session"
                });

        var user =
            await _context.Users.FindAsync(
                userId);

        if (user == null)
            return NotFound();

        _context.Users.Remove(user);

        await _context.SaveChangesAsync();

        return Ok(
            new
            {
                message = "Account Deleted"
            });
    }

    private static string? NormalizeOptional(string value)
    {
        var trimmed = value.Trim();

        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claimValue =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier);

        return Guid.TryParse(claimValue, out userId);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static object ToProfileResponse(AuthApi.Models.User user)
    {
        return new
        {
            user.Id,
            user.Name,
            user.Email,
            user.PhoneNumber,
            user.Bio,
            user.Address,
            user.DateOfBirth,
            user.ProfileImageUrl,
            user.CreatedAt,
            IsProfileComplete =
                !string.IsNullOrWhiteSpace(user.Name)
                && !string.IsNullOrWhiteSpace(user.Email)
                && !string.IsNullOrWhiteSpace(user.PhoneNumber)
                && !string.IsNullOrWhiteSpace(user.Address)
                && user.DateOfBirth.HasValue
        };
    }
}
