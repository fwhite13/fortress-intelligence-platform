using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Domain;

public class LifecycleCommandService
{
    private readonly FamOsDbContext _db;
    private readonly SignalResolver _signals;
    private readonly ILogger<LifecycleCommandService> _logger;

    public LifecycleCommandService(FamOsDbContext db, SignalResolver signals,
        ILogger<LifecycleCommandService> logger)
    {
        _db      = db;
        _signals = signals;
        _logger  = logger;
    }

    public async Task PursueOpportunityAsync(Guid opportunityId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.Intake,
                "PursueOpportunity requires stage INTAKE");
            Validate(!HasFlag(opp, OpportunityFlagType.Lost),
                "Cannot pursue a Lost opportunity");
            Validate(!string.IsNullOrEmpty(opp.OwnerUserId),
                "Opportunity must have an owner before pursuing");

            var from = opp.LifecycleStage;
            opp.LifecycleStage          = LifecycleStage.UnderwritingPrep;
            opp.LastStageTransitionAt   = DateTime.UtcNow;
            opp.UpdatedAt               = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "lifecycle_advanced",
                $"Advanced to {opp.LifecycleStage}", actorUserId);
            await WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, new {
                opportunity_id = opp.Id,
                from_stage     = from.ToString(),
                to_stage       = opp.LifecycleStage.ToString(),
                actor_user_id  = actorUserId,
                occurred_at    = DateTime.UtcNow
            });

            await CreateTasksForStageAsync(opp.Id, LifecycleStage.UnderwritingPrep);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task RouteToMarketAsync(Guid opportunityId, string[] carrierNames, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.UnderwritingPrep,
                "RouteToMarket requires stage UNDERWRITING_PREP");

            // Stage gate: must have at least one submission before routing to market
            if (!opp.Submissions.Any())
                throw new LifecycleValidationException(
                    "At least one carrier submission must be created before routing to market.");

            var from = opp.LifecycleStage;
            opp.LifecycleStage          = LifecycleStage.Marketed;
            opp.MarketedAt              = DateTime.UtcNow;
            opp.LastStageTransitionAt   = DateTime.UtcNow;
            opp.UpdatedAt               = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "routed_to_market",
                $"Routed to market with {opp.Submissions.Count} submission(s)", actorUserId);
            await WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, new {
                opportunity_id = opp.Id, from_stage = from.ToString(),
                to_stage = opp.LifecycleStage.ToString(), actor_user_id = actorUserId,
                occurred_at = DateTime.UtcNow
            });

            await CreateTasksForStageAsync(opp.Id, LifecycleStage.Marketed);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task RecordQuoteAsync(Guid opportunityId, Guid submissionId,
        string carrierName, decimal premium, string? coverageDetailsJson, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            // Verify the submissionId belongs to this opportunity
            if (!opp.Submissions.Any(s => s.Id == submissionId))
                throw new LifecycleValidationException("Submission not found on this opportunity.");

            var isFirst = !opp.Quotes.Any();

            _db.Quotes.Add(new Quote {
                OpportunityId   = opportunityId,
                SubmissionId    = submissionId,
                CarrierName     = carrierName,
                PremiumAmount   = premium,
                CoverageDetails = coverageDetailsJson,
                ReceivedAt      = DateTime.UtcNow
            });

            if (isFirst && opp.LifecycleStage == LifecycleStage.Marketed)
            {
                var from = opp.LifecycleStage;
                opp.LifecycleStage          = LifecycleStage.QuotesReceived;
                opp.LastStageTransitionAt   = DateTime.UtcNow;
                opp.UpdatedAt               = DateTime.UtcNow;
                opp.Version++;
                await WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, new {
                    opportunity_id = opp.Id, from_stage = from.ToString(),
                    to_stage = opp.LifecycleStage.ToString(), actor_user_id = actorUserId,
                    occurred_at = DateTime.UtcNow
                });
                await CreateTasksForStageAsync(opp.Id, LifecycleStage.QuotesReceived);
            }

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "quote_recorded",
                $"Quote from {carrierName}: ${premium:N0}", actorUserId);
            await WriteOutboxAsync(DomainEventType.QuoteRecorded, new {
                opportunity_id = opportunityId, carrier = carrierName, premium, occurred_at = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task SendProposalAsync(Guid opportunityId, Guid recommendedQuoteId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.QuotesReceived,
                "SendProposal requires stage QUOTES_RECEIVED");
            Validate(opp.Quotes.Any(q => q.Id == recommendedQuoteId),
                "Recommended quote not found on this opportunity");

            foreach (var q in opp.Quotes) q.IsRecommended = false;
            opp.Quotes.First(q => q.Id == recommendedQuoteId).IsRecommended = true;

            var proposal = new Proposal {
                OpportunityId      = opportunityId,
                RecommendedQuoteId = recommendedQuoteId,
                Status             = "sent",
                SentAt             = DateTime.UtcNow
            };
            _db.Proposals.Add(proposal);

            var from = opp.LifecycleStage;
            opp.LifecycleStage          = LifecycleStage.ClientDecision;
            opp.ProposalSentAt          = DateTime.UtcNow;
            opp.LastStageTransitionAt   = DateTime.UtcNow;
            opp.UpdatedAt               = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "proposal_sent", "Proposal sent to client", actorUserId);
            await WriteOutboxAsync(DomainEventType.ProposalSent, new {
                opportunity_id = opportunityId, proposal_id = proposal.Id,
                sent_at = DateTime.UtcNow
            });

            await CreateTasksForStageAsync(opp.Id, LifecycleStage.ClientDecision);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task ReopenMarketAsync(Guid opportunityId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.ClientDecision,
                "ReopenMarket requires stage CLIENT_DECISION");

            var from = opp.LifecycleStage;
            opp.LifecycleStage          = LifecycleStage.QuotesReceived;
            opp.LastStageTransitionAt   = DateTime.UtcNow;
            opp.UpdatedAt               = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "market_reopened", "Market reopened for negotiation", actorUserId);
            await WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, new {
                opportunity_id = opportunityId, from = from.ToString(), to = opp.LifecycleStage.ToString(), actor_user_id = actorUserId
            });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task RequestBindAsync(Guid opportunityId, Guid winningQuoteId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.ClientDecision,
                "RequestBind requires stage CLIENT_DECISION");
            Validate(opp.Quotes.Any(q => q.Id == winningQuoteId),
                "Winning quote not found");

            var from = opp.LifecycleStage;
            opp.LifecycleStage          = LifecycleStage.Binding;
            opp.ClientDecisionAt        = DateTime.UtcNow;
            opp.LastStageTransitionAt   = DateTime.UtcNow;
            opp.UpdatedAt               = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "bind_requested", "Bind requested from carrier", actorUserId);
            await WriteOutboxAsync(DomainEventType.BindRequested, new {
                opportunity_id = opportunityId, winning_quote_id = winningQuoteId
            });

            await CreateTasksForStageAsync(opp.Id, LifecycleStage.Binding);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task RecordBinderReceivedAsync(Guid opportunityId,
        DateOnly effectiveDate, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.Binding,
                "RecordBinderReceived requires stage BINDING");

            var winningQuote = opp.Quotes.FirstOrDefault(q => q.IsRecommended);
            var shadow = new PolicyShadowRecord {
                OpportunityId       = opportunityId,
                WinningQuoteId      = winningQuote?.Id,
                CarrierName         = winningQuote?.CarrierName,
                PolicyEffectiveDate = effectiveDate,
                PremiumAmount       = winningQuote?.PremiumAmount,
                RenewalTimerStart   = effectiveDate,
                SnapshotJson        = winningQuote?.CoverageDetails
            };
            _db.PolicyShadowRecords.Add(shadow);

            var from = opp.LifecycleStage;
            opp.LifecycleStage          = LifecycleStage.Bound;
            opp.LastStageTransitionAt   = DateTime.UtcNow;
            opp.UpdatedAt               = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "binder_received", "Binder received — policy bound", actorUserId);
            await WriteOutboxAsync(DomainEventType.BinderReceived, new {
                opportunity_id = opportunityId, policy_shadow_id = shadow.Id, effective_date = effectiveDate
            });

            await CreateTasksForStageAsync(opp.Id, LifecycleStage.Bound);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task ParkOpportunityAsync(Guid opportunityId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);

            Validate(!opp.IsClosed, "Cannot park a closed opportunity");

            var existing = opp.Flags.Where(f => f.FlagType == OpportunityFlagType.Parked && f.IsActive).ToList();
            existing.ForEach(f => f.IsActive = false);
            _db.OpportunityFlags.Add(new OpportunityFlag {
                OpportunityId = opportunityId,
                FlagType      = OpportunityFlagType.Parked,
            });

            opp.UpdatedAt = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "opportunity_parked", "Opportunity parked", actorUserId);
            await WriteOutboxAsync(DomainEventType.OpportunityParked, new {
                opportunity_id = opportunityId, actor_user_id = actorUserId
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task CloseOpportunityAsync(
        Guid opportunityId,
        CloseReason reason,
        string? notes,
        string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);

            Validate(!opp.IsClosed, "Opportunity already closed");

            var from = opp.LifecycleStage;
            opp.LifecycleStage          = LifecycleStage.ClosedNotBound;
            opp.IsClosed                = true;
            opp.CloseReason             = reason;
            opp.CloseNotes              = notes;
            opp.LastStageTransitionAt   = DateTime.UtcNow;
            opp.UpdatedAt               = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "opportunity_closed",
                $"Closed: {reason}{(notes != null ? " — " + notes : "")}", actorUserId);
            await WriteOutboxAsync(DomainEventType.OpportunityClosed, new {
                opportunityId, reason = reason.ToString(), notes
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    /// <summary>
    /// Saves intake questionnaire responses to the opportunity.
    /// Does NOT advance the lifecycle stage. Can be called multiple times (draft saves).
    /// </summary>
    public async Task SaveIntakeResponsesAsync(
        Guid opportunityId,
        Dictionary<string, string> responses,
        string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);

            opp.IntakeResponsesJson = System.Text.Json.JsonSerializer.Serialize(responses);
            opp.UpdatedAt           = DateTime.UtcNow;

            await WriteActivityAsync(opp.Id, "intake_saved",
                "Intake questionnaire saved", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    /// <summary>
    /// Creates a new carrier submission on an opportunity.
    /// Does NOT advance lifecycle stage. Can be called multiple times.
    /// </summary>
    public async Task<Guid> CreateSubmissionAsync(
        Guid opportunityId,
        string carrierName,
        string? coverageTypes,
        string? notes,
        string actorUserId)
    {
        var subId = Guid.Empty;
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);

            Validate(!opp.IsClosed, "Cannot add submissions to a closed opportunity");
            Validate(opp.LifecycleStage == LifecycleStage.UnderwritingPrep
                  || opp.LifecycleStage == LifecycleStage.Marketed,
                "Submissions can only be added during Underwriting Prep or while Marketed");

            var sub = new Submission
            {
                OpportunityId = opportunityId,
                CarrierName   = carrierName.Trim(),
                CoverageTypes = coverageTypes,
                Notes         = notes,
                Status        = SubmissionStatus.Pending,
            };
            _db.Submissions.Add(sub);
            opp.UpdatedAt = DateTime.UtcNow;

            await WriteActivityAsync(opp.Id, "submission_created",
                $"Submission added: {carrierName}", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            subId = sub.Id;
        });
        return subId;
    }

    /// <summary>
    /// Updates the status of a carrier submission (e.g., Pending → QuoteReceived).
    /// </summary>
    public async Task UpdateSubmissionStatusAsync(
        Guid submissionId,
        SubmissionStatus newStatus,
        string? notes,
        string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var sub = await _db.Submissions
                .Include(s => s.Opportunity)
                .FirstOrDefaultAsync(s => s.Id == submissionId)
                ?? throw new NotFoundException($"Submission {submissionId} not found");

            sub.Status    = newStatus;
            sub.Notes     = notes ?? sub.Notes;
            sub.UpdatedAt = DateTime.UtcNow;

            if (newStatus == SubmissionStatus.Sent)
                sub.SubmittedAt = sub.SubmittedAt ?? DateTime.UtcNow;
            if (newStatus is SubmissionStatus.QuoteReceived or SubmissionStatus.Declined)
                sub.RespondedAt = DateTime.UtcNow;

            await WriteActivityAsync(sub.OpportunityId, "submission_updated",
                $"{sub.CarrierName} → {newStatus}", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task SaveSubmissionScraperResultAsync(
        Guid submissionId, string resultJson, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var sub = await _db.Submissions.FindAsync(submissionId)
                ?? throw new NotFoundException($"Submission {submissionId} not found");

            sub.QuoteResultJson = resultJson;
            sub.UpdatedAt       = DateTime.UtcNow;

            await WriteActivityAsync(sub.OpportunityId, "quote_scraped",
                $"Quote PDF scraped for {sub.CarrierName}", actorUserId);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Opportunity> LoadOpportunityAsync(Guid id)
    {
        var opp = await _db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new NotFoundException($"Opportunity {id} not found");
        return opp;
    }

    private async Task<Opportunity> LoadOpportunityWithDetailsAsync(Guid id)
    {
        var opp = await _db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Quotes)
            .Include(o => o.Submissions)
            .Include(o => o.Proposals)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new NotFoundException($"Opportunity {id} not found");
        return opp;
    }

    private static void Validate(bool condition, string message)
    {
        if (!condition) throw new LifecycleValidationException(message);
    }

    private static bool HasFlag(Opportunity opp, OpportunityFlagType type)
        => opp.Flags.Any(f => f.FlagType == type && f.IsActive);

    private async Task RecomputeSignalAsync(Opportunity opp)
    {
        var (signal, reason) = _signals.Resolve(opp);
        if (opp.DominantSignal != signal)
        {
            await WriteOutboxAsync(DomainEventType.DominantSignalChanged, new {
                opportunity_id  = opp.Id,
                previous_signal = opp.DominantSignal.ToString(),
                new_signal      = signal.ToString(),
                reason
            });
        }
        opp.DominantSignal       = signal;
        opp.DominantSignalReason = reason;
    }

    private async Task WriteActivityAsync(Guid oppId, string eventType,
        string description, string? actorUserId)
    {
        _db.Activities.Add(new Activity {
            OpportunityId = oppId,
            EventType     = eventType,
            Description   = description,
            ActorUserId   = actorUserId
        });
        await Task.CompletedTask;
    }

    private async Task WriteOutboxAsync(DomainEventType type, object payload)
    {
        _db.OutboxEvents.Add(new OutboxEvent {
            EventType   = type.ToString(),
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload)
        });
        await Task.CompletedTask;
    }

    private async Task CreateTasksForStageAsync(Guid opportunityId, LifecycleStage stage)
    {
        var templates = StageTaskTemplates.ForStage(stage);
        foreach (var title in templates)
        {
            _db.Tasks.Add(new FamOsTask
            {
                OpportunityId = opportunityId,
                Title         = title,
                Status        = "open",
            });
        }
        // Tasks are saved in the calling method's SaveChangesAsync() — do not call SaveChanges here.
        await Task.CompletedTask;
    }
}

public class LifecycleValidationException : Exception
{
    public LifecycleValidationException(string message) : base(message) { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
