using MudBlazor;

namespace FamOs.Web.Theme;

/// <summary>
/// Central icon registry for FAM OS. Use these constants in all components.
/// Never reference Icons.Material.* directly — change icon style here once, updates everywhere.
/// </summary>
public static class FamosIcons
{
    // Navigation
    public const string Dashboard = Icons.Material.Outlined.Dashboard;
    public const string Pipeline = Icons.Material.Outlined.ViewKanban;
    public const string Tasks = Icons.Material.Outlined.CheckBox;
    public const string Accounts = Icons.Material.Outlined.Business;

    // Actions
    public const string Add = Icons.Material.Outlined.Add;
    public const string Edit = Icons.Material.Outlined.Edit;
    public const string Delete = Icons.Material.Outlined.Delete;
    public const string Retry = Icons.Material.Outlined.Replay;
    public const string Close = Icons.Material.Outlined.Close;
    public const string Save = Icons.Material.Outlined.Save;
    public const string Upload = Icons.Material.Outlined.Upload;
    public const string Download = Icons.Material.Outlined.Download;
    public const string OpenInNew = Icons.Material.Outlined.OpenInNew;

    // Search / Filter
    public const string Search = Icons.Material.Outlined.Search;
    public const string Filter = Icons.Material.Outlined.FilterList;
    public const string Clear = Icons.Material.Outlined.Clear;

    // Status / Signals
    public const string Warning = Icons.Material.Outlined.Warning;
    public const string Check = Icons.Material.Outlined.Check;
    public const string Urgent = Icons.Material.Outlined.PriorityHigh;
    public const string AtRisk = Icons.Material.Outlined.ErrorOutline;
    public const string Clock = Icons.Material.Outlined.AccessTime;
    public const string CheckCircle = Icons.Material.Outlined.CheckCircle;

    // Lifecycle
    public const string Advance = Icons.Material.Outlined.ArrowForward;
    public const string ChevronRight = Icons.Material.Outlined.ChevronRight;
    public const string ChevronDown = Icons.Material.Outlined.ExpandMore;
    public const string ExpandMore = Icons.Material.Outlined.ExpandMore;
    public const string ExpandLess = Icons.Material.Outlined.ExpandLess;

    // Data
    public const string Dollar = Icons.Material.Outlined.AttachMoney;
    public const string Calendar = Icons.Material.Outlined.CalendarToday;
    public const string Person = Icons.Material.Outlined.Person;
    public const string Document = Icons.Material.Outlined.Description;
    public const string Note = Icons.Material.Outlined.Notes;
    public const string Contacts   = Icons.Material.Outlined.Contacts;
    public const string Attach     = Icons.Material.Outlined.AttachFile;
    public const string NoteAlt    = Icons.Material.Outlined.NoteAlt;
    public const string AssignUser = Icons.Material.Outlined.AssignmentInd;

    // Data Viz
    public const string BarChart = Icons.Material.Outlined.BarChart;
}
