using Microsoft.AspNetCore.Components;

namespace Inbox.Client;

/// <summary>
/// Centralised 401 / session-expired redirect helper.
/// Avoids duplicating token-clear + navigate logic across every page's catch block.
/// <para>
/// Not wired into <see cref="RefreshTokenHandler"/> (the DelegatingHandler) because the
/// generated <see cref="SliceApiClient"/> always throws <see cref="SliceApiClient.SliceApiException"/>
/// on non-success responses — navigating inside the handler would still propagate the exception
/// to the awaiting call-site, causing an unhandled error overlay. Each page keeps its own
/// <c>catch (SliceApiException ex) when (ex.StatusCode == Unauthorized)</c> block and delegates
/// the side-effects here.
/// </para>
/// <para>
/// The Login page's token-validation path uses its own inline catch to show "Invalid token."
/// rather than "session expired" — it must <strong>not</strong> call this helper.
/// </para>
/// </summary>
internal static class AuthRedirect
{
    public static async Task HandleUnauthorizedAsync(
        RefreshTokenHolder tokenHolder, NavigationManager nav)
    {
        await tokenHolder.SetAsync(null);
        nav.NavigateTo("/login?reason=expired");
    }
}
