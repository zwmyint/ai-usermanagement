using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UserManagement.UI.Contracts;
using UserManagement.UI.Services;
using UserManagement.UI.ViewModels.Profile;

namespace UserManagement.UI.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly IProfileApiService _profileApi;

    public ProfileController(IProfileApiService profileApi) => _profileApi = profileApi;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var profile = await _profileApi.GetProfileAsync(ct);
        return View(ToViewModel(profile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        try
        {
            var updated = await _profileApi.UpdateProfileAsync(new UpdateProfileRequest
            {
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                PhoneNumber = model.PhoneNumber
            }, ct);
            if (model.ProfilePicture is { Length: > 0 })
            {
                updated = await _profileApi.UploadProfilePictureAsync(model.ProfilePicture, ct);
                await RenewProfilePictureClaimAsync(updated.ProfilePicturePath);
            }

            TempData["SuccessMessage"] = "Profile updated.";
            return View(ToViewModel(updated));
        }
        catch (ApiException ex)
        {
            AddApiErrors(ex);
            return View(model);
        }
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        try
        {
            await _profileApi.ChangePasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = model.CurrentPassword,
                NewPassword = model.NewPassword,
                ConfirmPassword = model.ConfirmPassword
            }, ct);

            TempData["SuccessMessage"] = "Password changed successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            AddApiErrors(ex);
            return View(model);
        }
    }

    private static ProfileViewModel ToViewModel(UserDto user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        PhoneNumber = user.PhoneNumber,
        ProfilePicturePath = user.ProfilePicturePath,
        EmailConfirmed = user.EmailConfirmed,
        LastLoginAt = user.LastLoginAt,
        CreatedAt = user.CreatedAt,
        Roles = user.Roles
    };

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

    private async Task RenewProfilePictureClaimAsync(string? profilePicturePath)
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Properties is null) return;

        var claims = User.Claims.Where(claim => claim.Type != "profile_picture").ToList();
        if (!string.IsNullOrWhiteSpace(profilePicturePath))
            claims.Add(new Claim("profile_picture", profilePicturePath));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), result.Properties);
    }
}
