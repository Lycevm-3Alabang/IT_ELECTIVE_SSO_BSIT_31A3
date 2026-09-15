using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Models;

namespace Gateway.Services;

/// <summary>
/// Prevents inactive SSO users from completing password sign-in.
/// S6 login UI should display AuthenticationMessages.AccountSuspended when
/// the user exists but IsActive is false.
/// </summary>
public class ActiveUserSignInManager : SignInManager<ApplicationUser>
{
    public ActiveUserSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public override async Task<SignInResult> CheckPasswordSignInAsync(
        ApplicationUser user, string password, bool lockoutOnFailure)
    {
        if (!user.IsActive)
        {
            return SignInResult.NotAllowed;
        }

        return await base.CheckPasswordSignInAsync(user, password, lockoutOnFailure);
    }
}

public static class AuthenticationMessages
{
    public const string AccountSuspended = "Account Suspended";
}
