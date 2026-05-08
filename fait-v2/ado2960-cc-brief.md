# ADO#2960 — KB Integration: Gap 1 (Pills) + Gap 2 (KB Management Page)

## Working Directory
`/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`

---

## Gap 1: KB Pill Click Verification

Check `wwwroot/css/fortress.css` for this rule:
```css
.mud-overlay.mud-overlay-drawer {
    pointer-events: none !important;
}
```

This rule IS already present (from ADO#2939). So the overlay isn't eating clicks.

Now check `Components/Chat/ChatView.razor` — the KB pills already have:
```razor
<button @onclick="ToggleFortressKb" ... class="chat-kb-pill @(_fortressKbEnabled ? "chat-kb-pill-active" : "")">
<button @onclick="TogglePersonalKb" ... class="chat-kb-pill @(_personalKbEnabled ? "chat-kb-pill-active" : "")">
```

And in the scoped `<style>` block in ChatView.razor:
```css
.chat-kb-pill-active {
    border-color: var(--color-gold);
    background: var(--color-gold-muted);
    color: var(--color-gold);
}
```

The pills are functionally wired. The visual active state uses CSS variables correctly.

**Action for Gap 1:** The pills work. No code change needed. Confirm in Build Report.

---

## Gap 2: New KB Management Page

Create file: `Components/Pages/KnowledgeBase.razor`

### Key constraints:
- CSS variables ONLY — no hardcoded colors, font sizes, or spacing
- Use `IForgeKbService` which is already registered and injected
- Get entraOid same as Dashboard: `auth.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value ?? auth.User.FindFirst("oid")?.Value ?? ""`
- MudBlazor components throughout (MudTabs, MudTabPanel, MudCard, MudTextField, MudButton, MudFileUpload, MudProgressCircular, MudAlert, MudSnackbar via ISnackbar)
- File is in `FortressAI.V2.Web` namespace

### KbInfo record (already exists in IForgeKbService.cs):
```csharp
public record KbInfo(string KbId, string KbType, string Description, bool Writable);
public record KbMetadata(string KbId, string KbType, int DocumentCount, DateTime LastUpdated, string DataSourceId);
```

### IForgeKbService methods:
```csharp
Task<IReadOnlyList<KbInfo>> ListKbsAsync(string entraOid, CancellationToken ct = default);
Task<string> AddToKbAsync(string kbId, string content, Dictionary<string, string> metadata, CancellationToken ct = default);
Task<KbMetadata> GetKbMetadataAsync(string kbId, CancellationToken ct = default);
Task<IReadOnlyList<KbSearchResult>> SearchKbAsync(string kbId, string query, int topK = 5, CancellationToken ct = default);
```

### Page to create: `Components/Pages/KnowledgeBase.razor`

```razor
@page "/knowledge-base"
@attribute [Authorize]
@inject IForgeKbService ForgeKbService
@inject ISnackbar Snackbar
@inject ILogger<KnowledgeBase> Logger
[CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }
```

#### Tab 1: My KB
- Text add: MudTextField for content + "Add to My KB" button → calls `ForgeKbService.AddToKbAsync(personalKbId, content, new Dictionary<string,string>{{"source","web"}})`
- File upload: MudFileUpload → read text content → call `AddToKbAsync`  
  - For file upload: read as text (UTF-8), use content as the KB content
  - Max file size: 2MB for MVP (text files)
- Stats: call `GetKbMetadataAsync(personalKbId)` to show doc count
- Show KB description from KbInfo

#### Tab 2: Teams KB
- List all KBs from `ListKbsAsync` filtered to `KbType == "team"`
- Select a team KB → show content area
- Add text / upload file to selected team KB
- "New Team KB" button: show info alert — "To create a new Team KB, contact your administrator."
- Back button to return to team list

#### Tab 3: Corporate KB (read-only)
- Get corporate KB from `ListKbsAsync` filtered to `KbType == "corporate"` (first result)
- Show `GetKbMetadataAsync` stats: DocumentCount, LastUpdated
- Search: MudTextField + "Search" button → calls `SearchKbAsync(corpKbId, query, 5)` → display results as MudCards

### Nav wiring in MainLayout.razor

In `Components/Layout/MainLayout.razor`, find the MudNavMenu section:
```razor
<MudNavLink Href="/workspace" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.FolderOpen">Workspace</MudNavLink>
<MudNavLink Href="/connectors" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Cable">Connectors</MudNavLink>
```

Add after Workspace, before Connectors:
```razor
<MudNavLink Href="/knowledge-base" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Storage">Knowledge Base</MudNavLink>
```

---

## CSS Variable Rule (CRITICAL)
NO hardcoded colors, font sizes, or spacing. All values MUST use CSS variables from fortress.css:
- Colors: `var(--color-text-primary)`, `var(--color-text-secondary)`, `var(--color-gold)`, `var(--color-border)`, `var(--color-surface)`, `var(--color-success)`, `var(--color-error)`, `var(--color-warning)`, etc.
- Font sizes: `var(--text-sm)`, `var(--text-base)`, `var(--text-lg)`, etc.
- Spacing: `var(--space-1)` through `var(--space-12)`
- Radius: `var(--radius-sm)`, `var(--radius-md)`, `var(--radius-lg)`

---

## Full KnowledgeBase.razor content to create

Write the complete file at `Components/Pages/KnowledgeBase.razor`:

```razor
@page "/knowledge-base"
@attribute [Authorize]
@inject IForgeKbService ForgeKbService
@inject ISnackbar Snackbar
@inject ILogger<KnowledgeBase> Logger

<PageTitle>Knowledge Base — FAIT v2</PageTitle>

<MudContainer MaxWidth="MaxWidth.Large" Class="py-4">
    <div class="kb-page-header">
        <div class="kb-page-header__badge">FORGE</div>
        <div class="kb-page-header__title-row">
            <MudText Typo="Typo.h5" Style="font-weight: var(--font-semibold);">Knowledge Base</MudText>
        </div>
    </div>
    <MudText Typo="Typo.body2" Class="mb-4" Style="color: var(--color-text-secondary);">
        Manage your personal, team, and corporate knowledge entries.
    </MudText>

    @if (_loading)
    {
        <div class="kb-page-loading">
            <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
        </div>
    }
    else
    {
        <MudTabs Elevation="2" Rounded="true" ApplyEffectsToContainer="true" PanelClass="pa-4"
                 @bind-ActivePanelIndex="_activeTab">

            <!-- My KB Tab -->
            <MudTabPanel Text="My KB" Icon="@Icons.Material.Filled.Person">
                @if (_personalKb == null)
                {
                    <MudAlert Severity="Severity.Info">No personal Knowledge Base found. Your KB will be provisioned automatically.</MudAlert>
                }
                else
                {
                    <!-- Stats -->
                    @if (_personalMetadata != null)
                    {
                        <div class="kb-stats-row">
                            <MudChip T="string" Icon="@Icons.Material.Filled.LibraryBooks" Size="Size.Small" Color="Color.Primary" Variant="Variant.Outlined">
                                @_personalMetadata.DocumentCount docs
                            </MudChip>
                            <MudChip T="string" Icon="@Icons.Material.Filled.AccessTime" Size="Size.Small" Color="Color.Default" Variant="Variant.Outlined">
                                Updated @_personalMetadata.LastUpdated.ToLocalTime().ToString("MMM d, yyyy")
                            </MudChip>
                        </div>
                    }

                    <!-- Add text content -->
                    <MudCard Elevation="1" Class="mb-4">
                        <MudCardHeader>
                            <CardHeaderContent>
                                <MudText Typo="Typo.subtitle1" Style="font-weight: var(--font-semibold);">Add Text to My KB</MudText>
                            </CardHeaderContent>
                        </MudCardHeader>
                        <MudCardContent>
                            <MudTextField @bind-Value="_personalAddContent"
                                          Label="Content to add"
                                          Lines="5"
                                          Variant="Variant.Outlined"
                                          Placeholder="Paste or type content to add to your knowledge base..."
                                          FullWidth="true" />
                        </MudCardContent>
                        <MudCardActions>
                            <MudButton Variant="Variant.Filled" Color="Color.Primary"
                                       StartIcon="@Icons.Material.Filled.Add"
                                       Disabled="@(string.IsNullOrWhiteSpace(_personalAddContent) || _personalAdding)"
                                       OnClick="AddPersonalTextContent">
                                @if (_personalAdding)
                                {
                                    <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-2" />
                                    <span>Adding...</span>
                                }
                                else
                                {
                                    <span>Add to My KB</span>
                                }
                            </MudButton>
                        </MudCardActions>
                    </MudCard>

                    <!-- Upload document -->
                    <MudCard Elevation="1" Class="mb-4">
                        <MudCardHeader>
                            <CardHeaderContent>
                                <MudText Typo="Typo.subtitle1" Style="font-weight: var(--font-semibold);">Upload Document to My KB</MudText>
                            </CardHeaderContent>
                        </MudCardHeader>
                        <MudCardContent>
                            <MudText Typo="Typo.body2" Style="color: var(--color-text-secondary); margin-bottom: var(--space-3);">
                                Supported: TXT, MD, CSV (max 2 MB). Content will be extracted and added to your KB.
                            </MudText>
                            <MudFileUpload T="IBrowserFile"
                                           Accept=".txt,.md,.csv"
                                           FilesChanged="UploadPersonalDocument"
                                           Hidden="false"
                                           InputClass="absolute mud-width-full mud-height-full overflow-hidden z-10"
                                           InputStyle="opacity:0">
                                <ActivatorContent>
                                    <MudButton Variant="Variant.Outlined" Color="Color.Secondary"
                                               StartIcon="@Icons.Material.Filled.Upload"
                                               Disabled="_personalUploading">
                                        @if (_personalUploading)
                                        {
                                            <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-2" />
                                            <span>Uploading...</span>
                                        }
                                        else
                                        {
                                            <span>Upload Document</span>
                                        }
                                    </MudButton>
                                </ActivatorContent>
                            </MudFileUpload>
                        </MudCardContent>
                    </MudCard>
                }
            </MudTabPanel>

            <!-- Teams KB Tab -->
            <MudTabPanel Text="Teams" Icon="@Icons.Material.Filled.FolderShared">
                @if (_selectedTeamKb == null)
                {
                    <!-- Team list -->
                    <div class="kb-teams-header">
                        <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                                   StartIcon="@Icons.Material.Filled.Info"
                                   OnClick="ShowNewTeamInfo">
                            New Team KB
                        </MudButton>
                    </div>

                    @if (_showNewTeamInfo)
                    {
                        <MudAlert Severity="Severity.Info" Class="mb-4" ShowCloseIcon="true" CloseIconClicked="() => _showNewTeamInfo = false">
                            To create a new Team Knowledge Base, contact your administrator. Team KBs are provisioned at the organizational level.
                        </MudAlert>
                    }

                    @if (!_teamKbs.Any())
                    {
                        <div class="kb-empty-state">
                            <MudIcon Icon="@Icons.Material.Filled.FolderOff" Style="font-size: 48px; color: var(--color-text-secondary); opacity: 0.4;" />
                            <MudText Typo="Typo.body1" Style="color: var(--color-text-secondary); margin-top: var(--space-3);">
                                No team Knowledge Bases available. Contact your administrator to request one.
                            </MudText>
                        </div>
                    }
                    else
                    {
                        @foreach (var team in _teamKbs)
                        {
                            var t = team;
                            <MudCard Elevation="1" Class="mb-3" Style="cursor: pointer;" @onclick="() => SelectTeamKb(t)">
                                <MudCardContent>
                                    <div class="kb-team-card-row">
                                        <div>
                                            <MudText Typo="Typo.h6" Style="font-weight: var(--font-semibold);">@t.Description</MudText>
                                            <MudText Typo="Typo.caption" Style="color: var(--color-text-secondary);">ID: @t.KbId</MudText>
                                        </div>
                                        @if (t.Writable)
                                        {
                                            <MudChip T="string" Size="Size.Small" Color="Color.Success" Variant="Variant.Outlined">Writable</MudChip>
                                        }
                                        else
                                        {
                                            <MudChip T="string" Size="Size.Small" Color="Color.Default" Variant="Variant.Outlined">Read-only</MudChip>
                                        }
                                    </div>
                                </MudCardContent>
                            </MudCard>
                        }
                    }
                }
                else
                {
                    <!-- Team detail view -->
                    <div class="kb-detail-back">
                        <MudButton StartIcon="@Icons.Material.Filled.ArrowBack" Variant="Variant.Text" OnClick="BackToTeams">
                            Back to Teams
                        </MudButton>
                    </div>

                    <MudText Typo="Typo.h5" Style="font-weight: var(--font-bold); color: var(--color-gold); margin-bottom: var(--space-2);">
                        @_selectedTeamKb.Description
                    </MudText>

                    @if (_selectedTeamKb.Writable)
                    {
                        <!-- Add text -->
                        <MudCard Elevation="1" Class="mb-4">
                            <MudCardHeader>
                                <CardHeaderContent>
                                    <MudText Typo="Typo.subtitle1" Style="font-weight: var(--font-semibold);">Add Text to Team KB</MudText>
                                </CardHeaderContent>
                            </MudCardHeader>
                            <MudCardContent>
                                <MudTextField @bind-Value="_teamAddContent"
                                              Label="Content to add"
                                              Lines="5"
                                              Variant="Variant.Outlined"
                                              Placeholder="Paste or type content to add to this team knowledge base..."
                                              FullWidth="true" />
                            </MudCardContent>
                            <MudCardActions>
                                <MudButton Variant="Variant.Filled" Color="Color.Primary"
                                           StartIcon="@Icons.Material.Filled.Add"
                                           Disabled="@(string.IsNullOrWhiteSpace(_teamAddContent) || _teamAdding)"
                                           OnClick="AddTeamTextContent">
                                    @if (_teamAdding)
                                    {
                                        <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-2" />
                                        <span>Adding...</span>
                                    }
                                    else
                                    {
                                        <span>Add to Team KB</span>
                                    }
                                </MudButton>
                            </MudCardActions>
                        </MudCard>

                        <!-- Upload document -->
                        <MudCard Elevation="1" Class="mb-4">
                            <MudCardHeader>
                                <CardHeaderContent>
                                    <MudText Typo="Typo.subtitle1" Style="font-weight: var(--font-semibold);">Upload Document to Team KB</MudText>
                                </CardHeaderContent>
                            </MudCardHeader>
                            <MudCardContent>
                                <MudFileUpload T="IBrowserFile"
                                               Accept=".txt,.md,.csv"
                                               FilesChanged="UploadTeamDocument"
                                               Hidden="false"
                                               InputClass="absolute mud-width-full mud-height-full overflow-hidden z-10"
                                               InputStyle="opacity:0">
                                    <ActivatorContent>
                                        <MudButton Variant="Variant.Outlined" Color="Color.Secondary"
                                                   StartIcon="@Icons.Material.Filled.Upload"
                                                   Disabled="_teamUploading">
                                            @if (_teamUploading)
                                            {
                                                <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-2" />
                                                <span>Uploading...</span>
                                            }
                                            else
                                            {
                                                <span>Upload Document</span>
                                            }
                                        </MudButton>
                                    </ActivatorContent>
                                </MudFileUpload>
                            </MudCardContent>
                        </MudCard>
                    }
                    else
                    {
                        <MudAlert Severity="Severity.Info" Class="mb-4">This team Knowledge Base is read-only.</MudAlert>
                    }

                    <!-- Team KB Metadata -->
                    @if (_selectedTeamMetadata != null)
                    {
                        <div class="kb-stats-row">
                            <MudChip T="string" Icon="@Icons.Material.Filled.LibraryBooks" Size="Size.Small" Color="Color.Primary" Variant="Variant.Outlined">
                                @_selectedTeamMetadata.DocumentCount docs
                            </MudChip>
                            <MudChip T="string" Icon="@Icons.Material.Filled.AccessTime" Size="Size.Small" Color="Color.Default" Variant="Variant.Outlined">
                                Updated @_selectedTeamMetadata.LastUpdated.ToLocalTime().ToString("MMM d, yyyy")
                            </MudChip>
                        </div>
                    }
                }
            </MudTabPanel>

            <!-- Corporate KB Tab -->
            <MudTabPanel Text="Corporate KB" Icon="@Icons.Material.Filled.AccountBalance">
                @if (_corporateKb == null)
                {
                    <MudAlert Severity="Severity.Info">No corporate Knowledge Base is configured for this environment.</MudAlert>
                }
                else
                {
                    <MudText Typo="Typo.h6" Style="font-weight: var(--font-semibold); margin-bottom: var(--space-3);">Fortress Corporate Knowledge Base</MudText>
                    <MudText Typo="Typo.body2" Style="color: var(--color-text-secondary); margin-bottom: var(--space-4);">
                        Read-only. Managed by Fortress administrators.
                    </MudText>

                    @if (_corporateMetadata != null)
                    {
                        <div class="kb-stats-row mb-4">
                            <MudChip T="string" Icon="@Icons.Material.Filled.LibraryBooks" Size="Size.Small" Color="Color.Primary" Variant="Variant.Outlined">
                                @_corporateMetadata.DocumentCount documents
                            </MudChip>
                            <MudChip T="string" Icon="@Icons.Material.Filled.AccessTime" Size="Size.Small" Color="Color.Default" Variant="Variant.Outlined">
                                Last updated @_corporateMetadata.LastUpdated.ToLocalTime().ToString("MMM d, yyyy HH:mm")
                            </MudChip>
                        </div>
                    }

                    <!-- Search -->
                    <MudCard Elevation="1" Class="mb-4">
                        <MudCardHeader>
                            <CardHeaderContent>
                                <MudText Typo="Typo.subtitle1" Style="font-weight: var(--font-semibold);">Search Corporate KB</MudText>
                            </CardHeaderContent>
                        </MudCardHeader>
                        <MudCardContent>
                            <div class="kb-search-row">
                                <MudTextField @bind-Value="_corporateSearchQuery"
                                              Placeholder="Enter search query..."
                                              Variant="Variant.Outlined"
                                              Adornment="Adornment.Start"
                                              AdornmentIcon="@Icons.Material.Filled.Search"
                                              Clearable="true"
                                              FullWidth="true" />
                                <MudButton Variant="Variant.Filled" Color="Color.Primary"
                                           StartIcon="@Icons.Material.Filled.Search"
                                           Disabled="@(string.IsNullOrWhiteSpace(_corporateSearchQuery) || _corporateSearching)"
                                           OnClick="SearchCorporateKb">
                                    @if (_corporateSearching)
                                    {
                                        <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-2" />
                                        <span>Searching...</span>
                                    }
                                    else
                                    {
                                        <span>Search</span>
                                    }
                                </MudButton>
                            </div>
                        </MudCardContent>
                    </MudCard>

                    @if (_corporateSearchResults.Any())
                    {
                        <MudText Typo="Typo.subtitle2" Style="color: var(--color-text-secondary); margin-bottom: var(--space-3);">
                            @_corporateSearchResults.Count result(s) for "@_lastCorporateSearchQuery"
                        </MudText>
                        @foreach (var result in _corporateSearchResults)
                        {
                            <MudCard Elevation="1" Class="mb-3">
                                <MudCardContent>
                                    <MudText Typo="Typo.body2" Style="white-space: pre-wrap; overflow-wrap: break-word;">
                                        @(result.Content.Length > 500 ? result.Content[..500] + "…" : result.Content)
                                    </MudText>
                                    <MudText Typo="Typo.caption" Style="color: var(--color-text-secondary); margin-top: var(--space-2);">
                                        Relevance: @result.RelevanceScore.ToString("P0")
                                    </MudText>
                                </MudCardContent>
                            </MudCard>
                        }
                    }
                    else if (_corporateSearchPerformed)
                    {
                        <MudAlert Severity="Severity.Info">No results found for "@_lastCorporateSearchQuery".</MudAlert>
                    }
                }
            </MudTabPanel>

        </MudTabs>
    }
</MudContainer>

<style>
    .kb-page-header {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        margin-bottom: var(--space-4);
    }

    .kb-page-header__badge {
        background: var(--color-gold);
        color: var(--color-text-on-primary);
        font-weight: var(--font-bold);
        font-size: var(--text-xs);
        padding: var(--space-1) var(--space-2);
        border-radius: var(--radius-sm);
        letter-spacing: var(--tracking-wide);
    }

    .kb-page-header__title-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
    }

    .kb-page-loading {
        display: flex;
        justify-content: center;
        padding: var(--space-12) 0;
    }

    .kb-stats-row {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-2);
        margin-bottom: var(--space-4);
    }

    .kb-empty-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        padding: var(--space-12) 0;
        text-align: center;
    }

    .kb-teams-header {
        display: flex;
        justify-content: flex-end;
        margin-bottom: var(--space-4);
    }

    .kb-team-card-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
    }

    .kb-detail-back {
        margin-bottom: var(--space-4);
    }

    .kb-search-row {
        display: flex;
        gap: var(--space-3);
        align-items: flex-start;
    }
</style>

@code {
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private bool _loading = true;
    private int _activeTab = 0;
    private string _entraOid = "";

    // Personal KB
    private KbInfo? _personalKb;
    private KbMetadata? _personalMetadata;
    private string _personalAddContent = "";
    private bool _personalAdding = false;
    private bool _personalUploading = false;

    // Teams KB
    private List<KbInfo> _teamKbs = new();
    private KbInfo? _selectedTeamKb;
    private KbMetadata? _selectedTeamMetadata;
    private string _teamAddContent = "";
    private bool _teamAdding = false;
    private bool _teamUploading = false;
    private bool _showNewTeamInfo = false;

    // Corporate KB
    private KbInfo? _corporateKb;
    private KbMetadata? _corporateMetadata;
    private string _corporateSearchQuery = "";
    private string _lastCorporateSearchQuery = "";
    private bool _corporateSearching = false;
    private bool _corporateSearchPerformed = false;
    private List<KbSearchResult> _corporateSearchResults = new();

    protected override async Task OnInitializedAsync()
    {
        if (AuthState != null)
        {
            var auth = await AuthState;
            _entraOid = auth.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                     ?? auth.User.FindFirst("oid")?.Value ?? "";
        }

        if (!string.IsNullOrEmpty(_entraOid))
        {
            try
            {
                var kbs = await ForgeKbService.ListKbsAsync(_entraOid);
                _personalKb = kbs.FirstOrDefault(k => k.KbType == "personal");
                _teamKbs = kbs.Where(k => k.KbType == "team").ToList();
                _corporateKb = kbs.FirstOrDefault(k => k.KbType == "corporate");

                // Load metadata in parallel (non-fatal)
                var metadataTasks = new List<Task>();
                if (_personalKb != null)
                    metadataTasks.Add(LoadPersonalMetadataAsync(_personalKb.KbId));
                if (_corporateKb != null)
                    metadataTasks.Add(LoadCorporateMetadataAsync(_corporateKb.KbId));
                await Task.WhenAll(metadataTasks);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load KB list for user {UserId}", _entraOid);
                Snackbar.Add("Failed to load Knowledge Base information. Please refresh.", Severity.Warning);
            }
        }

        _loading = false;
    }

    private async Task LoadPersonalMetadataAsync(string kbId)
    {
        try { _personalMetadata = await ForgeKbService.GetKbMetadataAsync(kbId); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to load personal KB metadata"); }
    }

    private async Task LoadCorporateMetadataAsync(string kbId)
    {
        try { _corporateMetadata = await ForgeKbService.GetKbMetadataAsync(kbId); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to load corporate KB metadata"); }
    }

    private async Task AddPersonalTextContent()
    {
        if (_personalKb == null || string.IsNullOrWhiteSpace(_personalAddContent)) return;
        _personalAdding = true;
        StateHasChanged();
        try
        {
            var metadata = new Dictionary<string, string>
            {
                { "source", "web-manual" },
                { "added_by", _entraOid },
                { "added_at", DateTime.UtcNow.ToString("O") }
            };
            await ForgeKbService.AddToKbAsync(_personalKb.KbId, _personalAddContent.Trim(), metadata);
            Snackbar.Add("Content added to your Knowledge Base.", Severity.Success);
            _personalAddContent = "";
            // Refresh metadata
            if (_personalKb != null) await LoadPersonalMetadataAsync(_personalKb.KbId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to add content to personal KB");
            Snackbar.Add($"Failed to add content: {ex.Message}", Severity.Error);
        }
        finally
        {
            _personalAdding = false;
            StateHasChanged();
        }
    }

    private async Task UploadPersonalDocument(IBrowserFile file)
    {
        if (_personalKb == null) return;
        _personalUploading = true;
        StateHasChanged();
        try
        {
            const long maxSize = 2 * 1024 * 1024; // 2MB
            using var stream = file.OpenReadStream(maxSize);
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            var content = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(content))
            {
                Snackbar.Add("File appears to be empty.", Severity.Warning);
                return;
            }

            var metadata = new Dictionary<string, string>
            {
                { "source", "file-upload" },
                { "filename", file.Name },
                { "added_by", _entraOid },
                { "added_at", DateTime.UtcNow.ToString("O") }
            };
            await ForgeKbService.AddToKbAsync(_personalKb.KbId, content, metadata);
            Snackbar.Add($"'{file.Name}' added to your Knowledge Base.", Severity.Success);
            if (_personalKb != null) await LoadPersonalMetadataAsync(_personalKb.KbId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to upload document to personal KB");
            Snackbar.Add($"Upload failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _personalUploading = false;
            StateHasChanged();
        }
    }

    private async Task SelectTeamKb(KbInfo teamKb)
    {
        _selectedTeamKb = teamKb;
        _selectedTeamMetadata = null;
        try { _selectedTeamMetadata = await ForgeKbService.GetKbMetadataAsync(teamKb.KbId); }
        catch (Exception ex) { Logger.LogWarning(ex, "Failed to load team KB metadata for {KbId}", teamKb.KbId); }
        StateHasChanged();
    }

    private void BackToTeams()
    {
        _selectedTeamKb = null;
        _selectedTeamMetadata = null;
        _teamAddContent = "";
    }

    private void ShowNewTeamInfo()
    {
        _showNewTeamInfo = true;
    }

    private async Task AddTeamTextContent()
    {
        if (_selectedTeamKb == null || string.IsNullOrWhiteSpace(_teamAddContent)) return;
        _teamAdding = true;
        StateHasChanged();
        try
        {
            var metadata = new Dictionary<string, string>
            {
                { "source", "web-manual" },
                { "added_by", _entraOid },
                { "added_at", DateTime.UtcNow.ToString("O") }
            };
            await ForgeKbService.AddToKbAsync(_selectedTeamKb.KbId, _teamAddContent.Trim(), metadata);
            Snackbar.Add("Content added to the Team Knowledge Base.", Severity.Success);
            _teamAddContent = "";
            if (_selectedTeamKb != null) _selectedTeamMetadata = await ForgeKbService.GetKbMetadataAsync(_selectedTeamKb.KbId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to add content to team KB {KbId}", _selectedTeamKb.KbId);
            Snackbar.Add($"Failed to add content: {ex.Message}", Severity.Error);
        }
        finally
        {
            _teamAdding = false;
            StateHasChanged();
        }
    }

    private async Task UploadTeamDocument(IBrowserFile file)
    {
        if (_selectedTeamKb == null) return;
        _teamUploading = true;
        StateHasChanged();
        try
        {
            const long maxSize = 2 * 1024 * 1024; // 2MB
            using var stream = file.OpenReadStream(maxSize);
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            var content = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(content))
            {
                Snackbar.Add("File appears to be empty.", Severity.Warning);
                return;
            }

            var metadata = new Dictionary<string, string>
            {
                { "source", "file-upload" },
                { "filename", file.Name },
                { "added_by", _entraOid },
                { "added_at", DateTime.UtcNow.ToString("O") }
            };
            await ForgeKbService.AddToKbAsync(_selectedTeamKb.KbId, content, metadata);
            Snackbar.Add($"'{file.Name}' added to the Team Knowledge Base.", Severity.Success);
            if (_selectedTeamKb != null) _selectedTeamMetadata = await ForgeKbService.GetKbMetadataAsync(_selectedTeamKb.KbId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to upload document to team KB {KbId}", _selectedTeamKb.KbId);
            Snackbar.Add($"Upload failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _teamUploading = false;
            StateHasChanged();
        }
    }

    private async Task SearchCorporateKb()
    {
        if (_corporateKb == null || string.IsNullOrWhiteSpace(_corporateSearchQuery)) return;
        _corporateSearching = true;
        _corporateSearchPerformed = false;
        _lastCorporateSearchQuery = _corporateSearchQuery;
        StateHasChanged();
        try
        {
            var results = await ForgeKbService.SearchKbAsync(_corporateKb.KbId, _corporateSearchQuery.Trim(), 5);
            _corporateSearchResults = results.ToList();
            _corporateSearchPerformed = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to search corporate KB");
            Snackbar.Add($"Search failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _corporateSearching = false;
            StateHasChanged();
        }
    }
}
```

---

## Final steps after creating KnowledgeBase.razor:

1. Edit `Components/Layout/MainLayout.razor` — add the Knowledge Base nav link after the Workspace nav link:
   Find: `<MudNavLink Href="/connectors" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Cable">Connectors</MudNavLink>`
   Insert before it: `<MudNavLink Href="/knowledge-base" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Storage">Knowledge Base</MudNavLink>`

2. Run `dotnet build` from `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/` — must get 0 errors, 0 warnings.

3. If there are errors, fix them. Common issues:
   - Missing using directives (check _Imports.razor — it already has `using FortressAI.V2.Web.Services`)
   - ILogger<KnowledgeBase> — the class name in the logger must match the razor component class name
   - KbSearchResult — already defined in IForgeKbService.cs

4. Run `git add -A && git commit -m "ADO#2960: KB pills verified functional; add /knowledge-base page with My KB, Teams, and Corporate KB tabs"` from the repo root.

Done. Report back with the build output and commit hash.
