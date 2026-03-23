# Build Report: WI908 — Sprint 8

## Status: COMPLETE ✅
**Commit:** `4efa808`
**Push:** `f27d8d8..4efa808 main → main`

## CC CLI Invocations
Session 1 completed (Parts B, A, C, D, E, F, G — all 7 parts).
Note: Tony's subagent timed out before producing report; Maria verified working tree and committed directly after confirming all constraints passed.

Session 1 pipe mode: `cat /tmp/wi908-cc-s1.md | claude --model sonnet -p`

## Changes

### New Files (5)
| File | Notes |
|------|-------|
| `Services/UserAffinityService.cs` | Multi-affinity lookup: claims → appsettings map → fallback |
| `Services/AccountSyncService.cs` | BackgroundService; IServiceScopeFactory pattern; RefreshOppCountsAsync |
| `Data/Entities/Account.cs` | Account entity with AffinityId, CompanyName, HubSpotId, ActiveOppCount |
| `Components/Pages/Accounts.razor` | Accounts page; IDbContextFactory<FamOsDbContext>; filter by affinity |
| `Components/Shared/PanelErrorBoundary.razor` | @inherits ErrorBoundary; Recover() inherited; ShowDetails param |

### Modified Files (19)
| File | Change |
|------|--------|
| `AffinityConfig.cs` | AffinityGroups list + UserAffinityMap dictionary added |
| `Components/Layout/MainLayout.razor` | AffinitySvc injection; affinity-aware portal name/logo |
| `Components/Layout/NavMenu.razor` | Accounts menu item activated |
| `Components/Pages/Accounts.razor` | (new — listed above) |
| `Components/Pages/Opportunity/OpportunityWorkspace.razor` | PanelErrorBoundary wrapping all 21 panel slots |
| `Components/Pages/Opportunity/Panels/MarketedPanel.razor` | Empty state (famos-empty-state) when no submissions |
| `Components/Pages/Opportunity/Panels/UnderwritingPrepPanel.razor` | Empty state for submissions list |
| `Components/Pages/Pipeline.razor` | Pagination (GetStagePageAsync); famos-pipeline-empty column empty state |
| `Components/Pages/TaskCenter.razor` | Pagination (GetOpenTasksPagedAsync); FamosIcons.CheckCircle empty state |
| `Components/Routes.razor` | @rendermode on Routes only — verified unchanged |
| `Data/Entities/Opportunity.cs` | AffinityId property added |
| `Data/FamOsDbContext.cs` | Account entity EF config; Opportunity.AffinityId HasColumnName("affinity_id") |
| `Domain/LifecycleCommandService.cs` | HubSpot fire-and-forget in AssignOwnerAsync + CloseOpportunityAsync |
| `Program.cs` | AccountSyncService registered; UserAffinityService registered; accounts DDL; affinity_id column migration |
| `Services/HubSpotService.cs` | SyncOwnerAsync + SyncClosedAsync + ResolveHubSpotUserIdAsync real impl |
| `Services/HubSpotServiceStub.cs` | SyncOwnerAsync + SyncClosedAsync stub methods |
| `Services/OpportunityService.cs` | GetStagePageAsync, GetStageSummaryAsync, AsSplitQuery on GetByIdAsync, DB-side aggregations in GetDashboardSummaryAsync, UserAffinityService injection |
| `Services/TaskService.cs` | GetOpenTasksPagedAsync added |
| `appsettings.json` | AffinityGroups (tig, iaapa, nbais) + UserAffinityMap + HubSpot:ServiceKey |
| `wwwroot/css/famos.css` | famos-empty-state, famos-pipeline-empty, famos-error-card, famos-error-detail |

## DB Migrations Added (Program.cs)
1. `ALTER TABLE opportunities ADD COLUMN affinity_id VARCHAR(50) NOT NULL DEFAULT 'tig'` — try/catch 1060
2. `CREATE TABLE IF NOT EXISTS accounts (...)` — id, affinity_id, company_name, hubspot_id, active_opp_count, last_synced_at, created_at, updated_at

## EF HasColumnName() Mappings Added (FamOsDbContext)
- `Account.AffinityId` → `affinity_id`
- `Account.CompanyName` → `company_name`
- `Account.HubSpotId` → `hubspot_id`
- `Account.ActiveOppCount` → `active_opp_count`
- `Account.LastSyncedAt` → `last_synced_at`
- `Account.CreatedAt` → `created_at`
- `Account.UpdatedAt` → `updated_at`
- `Opportunity.AffinityId` → `affinity_id`

## Self-Review Checklist
- [x] accounts table DDL in Program.cs (CREATE TABLE IF NOT EXISTS)
- [x] affinity_id column migration with try/catch 1060
- [x] Account entity EF config with full HasColumnName() for all snake_case cols
- [x] Opportunity.AffinityId HasColumnName("affinity_id")
- [x] AccountSyncService uses IServiceScopeFactory (5 references confirmed — no direct DbContext)
- [x] Accounts.razor uses IDbContextFactory<FamOsDbContext>
- [x] PanelErrorBoundary uses @inherits ErrorBoundary — Recover() is inherited (verified in file)
- [x] GetPipelineAsync() NOT modified — only GetStagePageAsync/GetStageSummaryAsync added
- [x] GetByIdAsync uses AsSplitQuery() (line 56)
- [x] GetDashboardSummaryAsync uses separate COUNT queries (DB-side aggregations)
- [x] UserAffinityService registered as scoped in Program.cs (line 123)
- [x] IAccountSyncService + AccountSyncService registered (singleton + hosted) (lines 124-125)
- [x] HubSpot fire-and-forget in AssignOwnerAsync + CloseOpportunityAsync (never awaited inside transaction)
- [x] PortalName = "TIG Dashboard" confirmed in appsettings.json
- [x] All empty states use famos-empty-state CSS class + FamosIcons.* icons
- [x] PanelErrorBoundary wrapping all 21 panel slots in OpportunityWorkspace
- [x] No files outside famos/src/FamOs.Web/ modified
- [x] Commit pushed to main
