using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskDesk.Domain;
using TaskDesk.Models;
using TaskDesk.Security;
using TaskDesk.Services;

namespace TaskDesk.Controllers;

public sealed class AccountController(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager,
    AuditService audit,
    ILogger<AccountController> logger) : Controller
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is { IsActive: true })
        {
            var result = await signInManager.PasswordSignInAsync(user, model.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                await audit.RecordAsync("LoginSucceeded", "User", user.Id, user.Email, cancellationToken);
                logger.LogInformation("User {Email} signed in", user.Email);

                // OWASP: only redirect to local URLs (prevents open-redirect phishing).
                return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
            }

            if (result.IsLockedOut)
            {
                await audit.RecordAsync("LoginLockedOut", "User", user.Id, user.Email, cancellationToken);
                logger.LogWarning("Locked-out account {Email} attempted to sign in", user.Email);
            }
        }

        await audit.RecordAsync("LoginFailed", "User", Truncate(model.Email), Truncate(model.Email), cancellationToken);

        // Same message for unknown user, wrong password, inactive or locked account (no user enumeration).
        ModelState.AddModelError(string.Empty, "Invalid email or password, or the account is temporarily locked.");
        model.Password = string.Empty;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await audit.RecordAsync("Logout", "User", User.Identity?.Name ?? "unknown", null, cancellationToken);
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult AccessDenied() => RedirectToAction("HttpStatus", "Home", new { code = 403 });

    private static string Truncate(string value) => value.Length <= 100 ? value : value[..100];
}
