using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PaymentGateway.API.DTOs.Responses;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Exchange an API key for a JWT bearer token
    /// </summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public IActionResult GetToken([FromBody] TokenRequest request)
    {
        var configuredKey = _configuration["Auth:ApiKey"];

        if (string.IsNullOrEmpty(configuredKey) || request.ApiKey != configuredKey)
            return Unauthorized(new ErrorResponse("INVALID_API_KEY", "Invalid API key"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:SecretKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddHours(8);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            expires: expiresAt,
            signingCredentials: creds);

        return Ok(new TokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt));
    }
}

public record TokenRequest([Required] string ApiKey);
public record TokenResponse(string Token, DateTime ExpiresAt);
