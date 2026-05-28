namespace FortressAI.Web.Services;

public record ArtifactRef(Guid Id, string S3Key, string Filename, string MimeType);

public class ChatLayoutState
{
    public bool ArtifactPanelOpen { get; private set; }
    public ArtifactRef? CurrentArtifact { get; private set; }
    public event Action? OnChange;

    public void OpenArtifactPreview(ArtifactRef artifact)
    {
        ArtifactPanelOpen = true;
        CurrentArtifact = artifact;
        OnChange?.Invoke();
    }

    public void CloseArtifactPreview()
    {
        ArtifactPanelOpen = false;
        CurrentArtifact = null;
        OnChange?.Invoke();
    }

    public bool ArtifactSidebarOpen { get; private set; }

    public void OpenArtifactSidebar()
    {
        ArtifactSidebarOpen = true;
        OnChange?.Invoke();
    }

    public void CloseArtifactSidebar()
    {
        ArtifactSidebarOpen = false;
        OnChange?.Invoke();
    }

    public void ToggleArtifactSidebar()
    {
        ArtifactSidebarOpen = !ArtifactSidebarOpen;
        OnChange?.Invoke();
    }
}
