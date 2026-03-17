using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CoworkWeb.Services;

/// <summary>
/// Scoped service that holds the current user's FIP identity for Blazor Server circuits.
/// Populated in MainLayout.razor during OnInitializedAsync — same pattern as FAIT's UserSessionService.
/// </summary>
public sealed class CoworkSessionService
{
    public string UserId   { get; private set; } = string.Empty;
    public string Email    { get; private set; } = string.Empty;
    public string Name     { get; private set; } = string.Empty;
    public bool   IsLoaded { get; private set; }

    public event Action? OnSessionChanged;

    public void SetFromClaims(ClaimsPrincipal user)
    {
        UserId  = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user.FindFirst("oid")?.Value
               ?? string.Empty;
        Email   = user.FindFirst(ClaimTypes.Email)?.Value
               ?? user.FindFirst("preferred_username")?.Value
               ?? string.Empty;
        Name    = user.FindFirst(ClaimTypes.Name)?.Value
               ?? user.FindFirst("name")?.Value
               ?? Email;
        IsLoaded = true;
        OnSessionChanged?.Invoke();
    }

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}
