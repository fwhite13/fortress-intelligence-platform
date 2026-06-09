namespace FortressAI.Web.Services;

public record ArtifactRef(Guid Id, string S3Key, string Filename, string MimeType, string? PreviewS3Key = null);

public class ChatLayoutState
{
    public bool ArtifactPanelOpen { get; private set; }
    public ArtifactRef? CurrentArtifact { get; private set; }
    public event Action? OnChange;

    public void OpenArtifactPreview(ArtifactRef artifact)
    {
        if (ArtifactPanelOpen && CurrentArtifact?.Id == artifact.Id) return;
        ArtifactPanelOpen = true;
        CurrentArtifact = artifact;
        OnChange?.Invoke();
    }

    public void CloseArtifactPreview()
    {
        if (!ArtifactPanelOpen && CurrentArtifact == null) return;
        ArtifactPanelOpen = false;
        CurrentArtifact = null;
        OnChange?.Invoke();
    }

    public bool ArtifactSidebarOpen { get; private set; }

    public void OpenArtifactSidebar()
    {
        if (ArtifactSidebarOpen) return;
        ArtifactSidebarOpen = true;
        OnChange?.Invoke();
    }

    public void CloseArtifactSidebar()
    {
        if (!ArtifactSidebarOpen) return;
        ArtifactSidebarOpen = false;
        OnChange?.Invoke();
    }

    public void ToggleArtifactSidebar()
    {
        ArtifactSidebarOpen = !ArtifactSidebarOpen;
        OnChange?.Invoke();
    }
}
