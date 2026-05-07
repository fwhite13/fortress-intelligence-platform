# BUILD Brief: ADO#2858 — FAIT v2: Workspace Explorer UI

**ADO WI:** #2858 (Fortress project)
**Repo:** `/home/fredw/projects/fip`
**Service:** `fait-v2/src/FortressAI.V2.Web/`
**Sprint:** FAIT v2 Sprint 4

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2858-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## Context

FAIT v2 stores user files in S3 under `workspaces/{userId}/`. This WI builds the `/workspace` route — a file explorer UI showing the user's S3-backed workspace with tree navigation, file preview, download, and delete.

**Existing services to use:**
- `IAmazonS3` — already registered via `AddAWSService<IAmazonS3>()`
- Config key: `AWS:WorkspaceBucket` (may be named `fortress-user-workspaces` or similar — read from config, don't hardcode)
- The `Workspace.razor` page stub already exists at `Components/Pages/Workspace.razor`

---

## Implementation

### 1. Add `IWorkspaceService` interface + `WorkspaceService`
File: `Services/IWorkspaceService.cs`

```csharp
public interface IWorkspaceService
{
    Task<List<WorkspaceFolder>> GetFolderStructureAsync(string userId, CancellationToken ct = default);
    Task<List<WorkspaceFile>> ListFilesAsync(string userId, string folder, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(string userId, string s3Key, CancellationToken ct = default);
    Task DeleteFileAsync(string userId, string s3Key, CancellationToken ct = default);
}

public class WorkspaceFolder
{
    public string Name { get; set; } = string.Empty;      // "artifacts", "uploads", "memory", "assistants"
    public string Prefix { get; set; } = string.Empty;    // S3 prefix
    public int FileCount { get; set; }
}

public class WorkspaceFile
{
    public string Key { get; set; } = string.Empty;       // Full S3 key
    public string FileName { get; set; } = string.Empty;  // Just the filename
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string Folder { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
}
```

File: `Services/WorkspaceService.cs`

```csharp
public class WorkspaceService : IWorkspaceService
{
    private static readonly string[] Folders = ["artifacts", "uploads", "memory", "assistants"];
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private string Bucket => _config["AWS:WorkspaceBucket"] ?? "fortress-user-workspaces";

    public async Task<List<WorkspaceFolder>> GetFolderStructureAsync(string userId, CancellationToken ct = default)
    {
        var result = new List<WorkspaceFolder>();
        foreach (var folder in Folders)
        {
            var prefix = $"workspaces/{userId}/{folder}/";
            var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = Bucket,
                Prefix = prefix,
                MaxKeys = 1000,
            }, ct);
            result.Add(new WorkspaceFolder
            {
                Name = folder,
                Prefix = prefix,
                FileCount = response.S3Objects.Count,
            });
        }
        return result;
    }

    public async Task<List<WorkspaceFile>> ListFilesAsync(string userId, string folder, CancellationToken ct = default)
    {
        var prefix = $"workspaces/{userId}/{folder}/";
        var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = prefix,
            MaxKeys = 500,
        }, ct);

        return response.S3Objects
            .Where(o => o.Key != prefix) // exclude folder marker
            .Select(o => new WorkspaceFile
            {
                Key = o.Key,
                FileName = Path.GetFileName(o.Key),
                SizeBytes = o.Size,
                LastModified = o.LastModified,
                Folder = folder,
                Extension = Path.GetExtension(o.Key).ToLowerInvariant(),
            })
            .OrderByDescending(f => f.LastModified)
            .ToList();
    }

    public async Task<string> GetDownloadUrlAsync(string userId, string s3Key, CancellationToken ct = default)
    {
        // Validate key belongs to this user
        if (!s3Key.StartsWith($"workspaces/{userId}/"))
            throw new UnauthorizedAccessException("Access denied");

        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Verb = HttpVerb.GET,
        };
        return _s3.GetPreSignedURL(request);
    }

    public async Task DeleteFileAsync(string userId, string s3Key, CancellationToken ct = default)
    {
        if (!s3Key.StartsWith($"workspaces/{userId}/"))
            throw new UnauthorizedAccessException("Access denied");

        await _s3.DeleteObjectAsync(Bucket, s3Key, ct);
    }
}
```

### 2. Update `Workspace.razor` page

The stub at `Components/Pages/Workspace.razor` needs a full implementation:

```razor
@page "/workspace"
@inject IWorkspaceService WorkspaceService
@inject AuthenticationStateProvider AuthState
@inject IJSRuntime JS

<PageTitle>Workspace — FAIT v2</PageTitle>

<div class="workspace-container">
    <div class="workspace-sidebar">
        <div class="workspace-sidebar__title">Workspace</div>
        @if (_folders != null)
        {
            @foreach (var folder in _folders)
            {
                <div class="workspace-folder-item @(_activeFolder == folder.Name ? "active" : "")"
                     @onclick="() => SelectFolder(folder.Name)">
                    <MudIcon Icon="@GetFolderIcon(folder.Name)" Size="Size.Small" />
                    <span>@folder.Name</span>
                    <span class="workspace-folder-count">@folder.FileCount</span>
                </div>
            }
        }
    </div>

    <div class="workspace-content">
        @if (_files == null)
        {
            <MudProgressCircular Indeterminate="true" />
        }
        else if (_files.Count == 0)
        {
            <div class="workspace-empty">
                <MudIcon Icon="@Icons.Material.Outlined.FolderOpen" Size="Size.Large" />
                <p>No files in @_activeFolder yet</p>
            </div>
        }
        else
        {
            <!-- Search -->
            <MudTextField @bind-Value="_searchQuery" Placeholder="Search files..." 
                          Adornment="Adornment.Start" 
                          AdornmentIcon="@Icons.Material.Outlined.Search"
                          Class="workspace-search" Immediate="true" />

            <!-- File list -->
            <div class="workspace-file-list">
                @foreach (var file in FilteredFiles)
                {
                    <div class="workspace-file-item">
                        <MudIcon Icon="@GetFileIcon(file.Extension)" Size="Size.Small" />
                        <div class="workspace-file-info">
                            <span class="workspace-file-name">@file.FileName</span>
                            <span class="workspace-file-meta">@FormatSize(file.SizeBytes) · @file.LastModified.ToString("MMM d, yyyy")</span>
                        </div>
                        <div class="workspace-file-actions">
                            @if (file.Extension == ".html")
                            {
                                <MudIconButton Icon="@Icons.Material.Outlined.Visibility"
                                               Size="Size.Small" Title="Preview"
                                               OnClick="() => PreviewFile(file)" />
                            }
                            <MudIconButton Icon="@Icons.Material.Outlined.Download"
                                           Size="Size.Small" Title="Download"
                                           OnClick="() => DownloadFile(file)" />
                            <MudIconButton Icon="@Icons.Material.Outlined.Delete"
                                           Size="Size.Small" Title="Delete" Color="Color.Error"
                                           OnClick="() => ConfirmDelete(file)" />
                        </div>
                    </div>
                }
            </div>
        }

        <!-- HTML Preview panel -->
        @if (_previewFile != null)
        {
            <div class="workspace-preview-panel">
                <div class="workspace-preview-header">
                    <span>@_previewFile.FileName</span>
                    <MudIconButton Icon="@Icons.Material.Outlined.Close"
                                   Size="Size.Small" OnClick="ClosePreview" />
                </div>
                <iframe src="@_previewUrl" class="workspace-preview-iframe" sandbox="allow-scripts" />
            </div>
        }
    </div>
</div>

<!-- Delete confirm dialog -->
<MudDialog @bind-IsVisible="_showDeleteDialog">
    <TitleContent>Delete file?</TitleContent>
    <DialogContent>
        <MudText>Delete <strong>@_fileToDelete?.FileName</strong>? This cannot be undone.</MudText>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="() => _showDeleteDialog = false">Cancel</MudButton>
        <MudButton Color="Color.Error" Variant="Variant.Filled" OnClick="DeleteConfirmed">Delete</MudButton>
    </DialogActions>
</MudDialog>

@code {
    private List<WorkspaceFolder>? _folders;
    private List<WorkspaceFile>? _files;
    private string _activeFolder = "artifacts";
    private string _searchQuery = string.Empty;
    private WorkspaceFile? _previewFile;
    private string? _previewUrl;
    private bool _showDeleteDialog;
    private WorkspaceFile? _fileToDelete;
    private string _userId = string.Empty;

    private IEnumerable<WorkspaceFile> FilteredFiles =>
        string.IsNullOrWhiteSpace(_searchQuery)
            ? _files ?? []
            : (_files ?? []).Where(f => f.FileName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        var auth = await AuthState.GetAuthenticationStateAsync();
        _userId = auth.User.FindFirst("oid")?.Value
               ?? auth.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
               ?? string.Empty;

        _folders = await WorkspaceService.GetFolderStructureAsync(_userId);
        await LoadFiles();
    }

    private async Task SelectFolder(string folder)
    {
        _activeFolder = folder;
        _files = null;
        await LoadFiles();
    }

    private async Task LoadFiles()
    {
        _files = await WorkspaceService.ListFilesAsync(_userId, _activeFolder);
    }

    private async Task DownloadFile(WorkspaceFile file)
    {
        var url = await WorkspaceService.GetDownloadUrlAsync(_userId, file.Key);
        await JS.InvokeVoidAsync("open", url, "_blank");
    }

    private async Task PreviewFile(WorkspaceFile file)
    {
        _previewUrl = await WorkspaceService.GetDownloadUrlAsync(_userId, file.Key);
        _previewFile = file;
    }

    private void ClosePreview() { _previewFile = null; _previewUrl = null; }

    private void ConfirmDelete(WorkspaceFile file)
    {
        _fileToDelete = file;
        _showDeleteDialog = true;
    }

    private async Task DeleteConfirmed()
    {
        if (_fileToDelete == null) return;
        await WorkspaceService.DeleteFileAsync(_userId, _fileToDelete.Key);
        _showDeleteDialog = false;
        _fileToDelete = null;
        await LoadFiles();
        _folders = await WorkspaceService.GetFolderStructureAsync(_userId);
    }

    private static string GetFolderIcon(string folder) => folder switch
    {
        "artifacts" => Icons.Material.Outlined.AutoAwesome,
        "uploads"   => Icons.Material.Outlined.Upload,
        "memory"    => Icons.Material.Outlined.Psychology,
        "assistants" => Icons.Material.Outlined.SmartToy,
        _ => Icons.Material.Outlined.Folder,
    };

    private static string GetFileIcon(string ext) => ext switch
    {
        ".docx" => Icons.Material.Outlined.Description,
        ".xlsx" => Icons.Material.Outlined.TableChart,
        ".pptx" => Icons.Material.Outlined.Slideshow,
        ".html" => Icons.Material.Outlined.Web,
        ".json" => Icons.Material.Outlined.DataObject,
        ".pdf"  => Icons.Material.Outlined.PictureAsPdf,
        _ => Icons.Material.Outlined.InsertDriveFile,
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024         => $"{bytes} B",
        < 1024 * 1024  => $"{bytes / 1024.0:F1} KB",
        _              => $"{bytes / (1024.0 * 1024):F1} MB",
    };
}
```

### 3. Add CSS to `wwwroot/css/app.css`

Add workspace styles using CSS variables only — no hardcoded colors:

```css
/* Workspace Explorer */
.workspace-container { display: flex; height: calc(100vh - 64px); }
.workspace-sidebar { width: 200px; border-right: 1px solid var(--color-border); padding: var(--spacing-sm); }
.workspace-sidebar__title { font-weight: 600; color: var(--color-text-secondary); font-size: var(--font-size-sm); text-transform: uppercase; margin-bottom: var(--spacing-sm); }
.workspace-folder-item { display: flex; align-items: center; gap: var(--spacing-xs); padding: var(--spacing-xs) var(--spacing-sm); border-radius: var(--border-radius-sm); cursor: pointer; color: var(--color-text); }
.workspace-folder-item:hover, .workspace-folder-item.active { background: var(--color-surface-hover); color: var(--color-primary); }
.workspace-folder-count { margin-left: auto; font-size: var(--font-size-xs); color: var(--color-text-secondary); }
.workspace-content { flex: 1; overflow-y: auto; padding: var(--spacing-md); }
.workspace-empty { display: flex; flex-direction: column; align-items: center; gap: var(--spacing-sm); color: var(--color-text-secondary); padding-top: var(--spacing-xl); }
.workspace-search { margin-bottom: var(--spacing-md); }
.workspace-file-list { display: flex; flex-direction: column; gap: var(--spacing-xs); }
.workspace-file-item { display: flex; align-items: center; gap: var(--spacing-sm); padding: var(--spacing-sm); border-radius: var(--border-radius-sm); border: 1px solid var(--color-border); background: var(--color-surface); }
.workspace-file-item:hover { background: var(--color-surface-hover); }
.workspace-file-info { flex: 1; min-width: 0; }
.workspace-file-name { display: block; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.workspace-file-meta { font-size: var(--font-size-xs); color: var(--color-text-secondary); }
.workspace-file-actions { display: flex; gap: 2px; flex-shrink: 0; }
.workspace-preview-panel { position: fixed; right: 0; top: 64px; width: 50%; height: calc(100vh - 64px); background: var(--color-surface); border-left: 1px solid var(--color-border); display: flex; flex-direction: column; z-index: 100; }
.workspace-preview-header { display: flex; align-items: center; justify-content: space-between; padding: var(--spacing-sm) var(--spacing-md); border-bottom: 1px solid var(--color-border); font-weight: 500; }
.workspace-preview-iframe { flex: 1; border: none; }
```

### 4. Register service in `Program.cs`

```csharp
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
```

---

## Constraints

- **CSS variables only** — no hardcoded colors/fonts/sizes
- **No Cognito** — auth via Entra OID claim
- **S3 key validation** — always verify key starts with `workspaces/{userId}/` before any S3 operation
- `using Amazon.S3.Model;` for the S3 request types

---

## Acceptance Criteria

- [ ] `/workspace` renders S3 file tree for authenticated user
- [ ] Folders: artifacts, uploads, memory, assistants
- [ ] Files list with name, size, last-modified
- [ ] HTML files have preview button → renders inline in iframe
- [ ] Download works via pre-signed URL
- [ ] Delete removes S3 object; list refreshes
- [ ] Empty state message when no files
- [ ] `IWorkspaceService` registered in `Program.cs`
- [ ] `dotnet build` 0 errors, 0 warnings
- [ ] All CSS via variables

---

## ADO Tracking (MANDATORY)

After build complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2858,
  "text": "**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED."
}'
```

---

## Deliverables

1. `Services/IWorkspaceService.cs` (new)
2. `Services/WorkspaceService.cs` (new)
3. `Components/Pages/Workspace.razor` (updated from stub)
4. `wwwroot/css/app.css` (workspace styles added)
5. `Program.cs` (service registration)
6. Build Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2858-BUILD-REPORT.md`
