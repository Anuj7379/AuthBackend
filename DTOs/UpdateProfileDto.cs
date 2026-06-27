namespace AuthApi.DTOs;

using System.ComponentModel.DataAnnotations;

public class UpdateProfileDto
{
    [StringLength(100, MinimumLength = 2)]
    public string? Name { get; set; }

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; set; }

    [StringLength(32)]
    public string? PhoneNumber { get; set; }

    [StringLength(500)]
    public string? Bio { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    [Url]
    [StringLength(500)]
    public string? ProfileImageUrl { get; set; }
}
