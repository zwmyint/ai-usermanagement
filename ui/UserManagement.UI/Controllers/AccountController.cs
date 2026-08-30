using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.UI.Contracts;
using UserManagement.UI.Security;
using UserManagement.UI.Services;
using UserManagement.UI.ViewModels.Account;

namespace UserManagement.UI.Controllers;

public class AccountController : Controller
{
    private readonly IAuthApiService _authApi;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAuthApiService authApi, ILogger<AccountController> logger)
    {
        _authApi = authApi;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        try
        {
            var auth = await _authApi.LoginAsync(new LoginRequest
            {
                UserNameOrEmail = model.UserNameOrEmail,
                Password = model.Password
            }, ct);

            await SignInAsync(auth);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToAction("Index", "Home");
        }
        catch (ApiException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        try
        {
            await _authApi.RegisterAsync(new RegisterRequest
            {
                UserName = model.UserName,
                Email = model.Email,
                Password = model.Password,
                ConfirmPassword = model.ConfirmPassword,
                FirstName = model.FirstName,
                LastName = model.LastName
            }, ct);

            TempData["SuccessMessage"] = "Registration successful. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }
        catch (ApiException ex)
        {
            AddApiErrors(ex);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var refreshToken = result.Properties?.Items.TryGetValue(AuthTokenNames.RefreshToken, out var rt) == true ? rt : null;

        if (!string.IsNullOrEmpty(refreshToken))
        {
            try { await _authApi.RevokeAsync(refreshToken, ct); }
            catch (ApiException ex) { _logger.LogWarning(ex, "Failed to revoke refresh token on logout."); }
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        // Always report success to avoid leaking which emails are registered.
        try { await _authApi.ForgotPasswordAsync(new ForgotPasswordRequest { Email = model.Email }, ct); }
        catch (ApiException ex) { _logger.LogWarning(ex, "ForgotPassword request failed."); }

        TempData["SuccessMessage"] = "If that email exists, a password reset link has been sent.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email = null, string? token = null) =>
        View(new ResetPasswordViewModel { Email = email ?? string.Empty, Token = token ?? string.Empty });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        try
        {
            await _authApi.ResetPasswordAsync(new ResetPasswordRequest
            {
                Email = model.Email,
                Token = model.Token,
                NewPassword = model.NewPassword,
                ConfirmPassword = model.ConfirmPassword
            }, ct);

            TempData["SuccessMessage"] = "Password reset. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }
        catch (ApiException ex)
        {
            AddApiErrors(ex);
            return View(model);
        }
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task SignInAsync(AuthResponse auth)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, auth.User.Id.ToString()),
            new(ClaimTypes.Name, auth.User.UserName),
            new(ClaimTypes.Email, auth.User.Email),
            new("full_name", auth.User.FullName)
        };
        if (!string.IsNullOrWhiteSpace(auth.User.ProfilePicturePath))
            claims.Add(new Claim("profile_picture", auth.User.ProfilePicturePath));
        claims.AddRange(auth.User.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = auth.RefreshTokenExpiresAt
        };
        properties.Items[AuthTokenNames.AccessToken] = auth.AccessToken;
        properties.Items[AuthTokenNames.RefreshToken] = auth.RefreshToken;
        properties.Items[AuthTokenNames.AccessTokenExpiresAt] = auth.AccessTokenExpiresAt.ToString("o");

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }

    private void AddApiErrors(ApiException ex)
    {
        if (ex.Errors is { Count: > 0 })
        {
            foreach (var (_, messages) in ex.Errors)
                foreach (var message in messages)
                    ModelState.AddModelError(string.Empty, message);
        }
        else
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
    }
}
