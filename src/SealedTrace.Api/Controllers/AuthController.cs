using System.Net;
using Arovyx.SealedTrace.Api.Validation;
using Arovyx.SealedTrace.Core.Dtos.Auth;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Auth;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arovyx.SealedTrace.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly AuthRegisterValidator _registerValidator;
    private readonly AuthLoginValidator _loginValidator;

    public AuthController(
        IAuthService authService,
        AuthRegisterValidator registerValidator,
        AuthLoginValidator loginValidator)
    {
        _authService = authService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] AuthRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _registerValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(validationResult);
        }

        try
        {
            var user = await _authService.RegisterAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Me), new { }, user);
        }
        catch (EmailTakenException)
        {
            return Conflict(Problem("email_taken", "Email is already taken.", HttpStatusCode.Conflict));
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] AuthLoginRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _loginValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(validationResult);
        }

        try
        {
            var response = await _authService.LoginAsync(request, Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, cancellationToken);

            Response.Cookies.Append("st_refresh", response.RefreshTokenPlaintext!, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/api/v1/auth",
                MaxAge = TimeSpan.FromDays(30),
            });

            return Ok(new
            {
                accessToken = response.AccessToken,
                tokenType = response.TokenType,
                expiresIn = response.ExpiresIn,
                user = response.User,
            });
        }
        catch (InvalidCredentialsException)
        {
            return Unauthorized(Problem("invalid_credentials", "Invalid email or password.", HttpStatusCode.Unauthorized));
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue("st_refresh", out var refreshToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(Problem("invalid_refresh_token", "Missing refresh token.", HttpStatusCode.Unauthorized));
        }

        try
        {
            var response = await _authService.RefreshAsync(refreshToken, Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, cancellationToken);

            Response.Cookies.Append("st_refresh", response.RefreshTokenPlaintext!, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/api/v1/auth",
                MaxAge = TimeSpan.FromDays(30),
            });

            return Ok(new
            {
                accessToken = response.AccessToken,
                tokenType = response.TokenType,
                expiresIn = response.ExpiresIn,
                user = response.User,
            });
        }
        catch (InvalidRefreshTokenException)
        {
            return Unauthorized(Problem("invalid_refresh_token", "Invalid or expired refresh token.", HttpStatusCode.Unauthorized));
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue("st_refresh", out var refreshToken))
        {
            await _authService.LogoutAsync(refreshToken, cancellationToken);
        }

        Response.Cookies.Delete("st_refresh", new CookieOptions
        {
            Path = "/api/v1/auth",
        });

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(Problem("invalid_token", "Invalid token.", HttpStatusCode.Unauthorized));
        }

        try
        {
            var user = await _authService.MeAsync(userId, cancellationToken);
            return Ok(user);
        }
        catch (UserNotFoundException)
        {
            return NotFound(Problem("not_found", "User not found.", HttpStatusCode.NotFound));
        }
    }

    private BadRequestObjectResult ValidationProblem(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors.Select(e => new
        {
            field = e.PropertyName,
            message = e.ErrorMessage,
        });

        return BadRequest(Problem(
            "validation_failed",
            "One or more fields are invalid.",
            HttpStatusCode.BadRequest,
            errors));
    }

    private static object Problem(string code, string title, HttpStatusCode status, object? errors = null)
    {
        return new
        {
            type = $"https://sealedtrace.dev/errors/{code}",
            title,
            status = (int)status,
            detail = title,
            code,
            traceId = System.Diagnostics.Activity.Current?.Id ?? string.Empty,
            errors,
        };
    }
}
