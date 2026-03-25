using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
using FamOs.Web.Services;

namespace FamOs.Web.Domain;

public class LifecycleCommandService : IUploadLifecycleService
{
    private readonly FamOsDbContext _db;
    private readonly SignalResolver _signals;
    private readonly IHubSpotService _hubspot;
    private readonly ILogger<LifecycleCommandService> _logger;

    public LifecycleCommandService(FamOsDbContext db, SignalResolver signals,
        IHubSpotService hubspot,
        ILogger<LifecycleCommandService> logger)
    {
        _db      = db;
        _signals = signals;
        _hubspot = hubspot;
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

            // UW completeness gate: requires >= 60%
            var completer = new FamOs.Web.Services.UwCompletenessService();
            var uwResult  = completer.Evaluate(opp);
            if (!uwResult.CanRouteToMarket)
            {
                var missing = string.Join("; ", uwResult.UnmetItems);
                throw new LifecycleValidationException(
                    $"UW completeness is {uwResult.Score}% — must reach 60% before routing to market. " +
                    $"Incomplete: {missing}");
            }

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
                ReceivedAt      = DateTime.UtcNow,
                TenantId        = 1   // single-tenant; matches HasQueryFilter in DbContext
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

    /// <summary>
    /// Creates a draft proposal linked to a recommended quote.
    /// Does NOT advance lifecycle stage. Must call MarkProposalSentAsync to advance.
    /// </summary>
    public async Task<Guid> CreateProposalAsync(
        Guid opportunityId,
        Guid recommendedQuoteId,
        string? notes,
        string actorUserId)
    {
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.QuotesReceived,
                "Proposals can only be created at the Quotes Received stage");
            Validate(opp.Quotes.Any(q => q.Id == recommendedQuoteId),
                "Recommended quote not found on this opportunity");

            // Mark quote as recommended
            foreach (var q in opp.Quotes) q.IsRecommended = false;
            var winningQuote = opp.Quotes.First(q => q.Id == recommendedQuoteId);
            winningQuote.IsRecommended = true;

            var proposal = new Proposal
            {
                OpportunityId      = opportunityId,
                RecommendedQuoteId = recommendedQuoteId,
                CarrierName        = winningQuote.CarrierName,
                CoverageTypes      = winningQuote.CoverageDetails,
                ProposalDate       = DateTime.UtcNow,
                Status             = "draft",
                Notes              = notes?.Trim(),
            };
            _db.Proposals.Add(proposal);
            opp.UpdatedAt = DateTime.UtcNow;

            await WriteActivityAsync(opp.Id, "proposal_created",
                $"Proposal drafted: {winningQuote.CarrierName} ${winningQuote.PremiumAmount:N0}",
                actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return proposal.Id;
        });
    }

    /// <summary>
    /// Marks a draft proposal as sent and advances lifecycle to CLIENT_DECISION.
    /// </summary>
    public async Task MarkProposalSentAsync(
        Guid opportunityId,
        Guid proposalId,
        string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.QuotesReceived,
                "MarkProposalSent requires stage QUOTES_RECEIVED");

            var proposal = opp.Proposals.FirstOrDefault(p => p.Id == proposalId)
                ?? throw new LifecycleValidationException("Proposal not found on this opportunity");
            Validate(proposal.Status == "draft",
                "Only draft proposals can be marked as sent");

            proposal.Status = "sent";
            proposal.SentAt = DateTime.UtcNow;

            opp.LifecycleStage        = LifecycleStage.ClientDecision;
            opp.ProposalSentAt        = DateTime.UtcNow;
            opp.LastStageTransitionAt = DateTime.UtcNow;
            opp.UpdatedAt             = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "proposal_sent",
                $"Proposal sent to client: {proposal.CarrierName}", actorUserId);
            await WriteOutboxAsync(DomainEventType.ProposalSent, new
            {
                opportunity_id = opportunityId,
                proposal_id    = proposalId,
                sent_at        = DateTime.UtcNow
            });
            await CreateTasksForStageAsync(opp.Id, LifecycleStage.ClientDecision);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    /// <summary>
    /// Records client acceptance or decline on a sent proposal.
    /// On acceptance: advances lifecycle to BINDING.
    /// On decline: marks proposal declined; opportunity stays at CLIENT_DECISION.
    /// </summary>
    public async Task RecordClientResponseAsync(
        Guid opportunityId,
        Guid proposalId,
        bool accepted,
        string? declineReason,
        string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.ClientDecision,
                "RecordClientResponse requires stage CLIENT_DECISION");

            var proposal = opp.Proposals.FirstOrDefault(p => p.Id == proposalId)
                ?? throw new LifecycleValidationException("Proposal not found");
            Validate(proposal.Status == "sent",
                "Can only record response on a sent proposal");

            proposal.Status            = accepted ? "accepted" : "declined";
            proposal.ClientDecisionAt  = DateTime.UtcNow;
            proposal.DeclineReason     = accepted ? null : declineReason;

            if (accepted)
            {
                var winningQuoteId = proposal.RecommendedQuoteId;
                Validate(opp.Quotes.Any(q => q.Id == winningQuoteId), "Winning quote not found");

                opp.LifecycleStage        = LifecycleStage.Binding;
                opp.ClientDecisionAt      = DateTime.UtcNow;
                opp.LastStageTransitionAt = DateTime.UtcNow;
                opp.UpdatedAt             = DateTime.UtcNow;
                opp.Version++;

                await RecomputeSignalAsync(opp);
                await WriteActivityAsync(opp.Id, "client_accepted",
                    $"Client accepted proposal: {proposal.CarrierName}", actorUserId);
                await WriteOutboxAsync(DomainEventType.BindRequested, new
                {
                    opportunity_id   = opportunityId,
                    winning_quote_id = winningQuoteId,
                    proposal_id      = proposalId
                });
                await CreateTasksForStageAsync(opp.Id, LifecycleStage.Binding);
            }
            else
            {
                await WriteActivityAsync(opp.Id, "client_declined",
                    $"Client declined proposal: {declineReason ?? "no reason given"}", actorUserId);
            }

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

    /// <summary>
    /// Saves bind tracking fields (confirmation number, submitted flag).
    /// Does NOT advance lifecycle. Stage must be BINDING.
    /// </summary>
    public async Task UpdateBindTrackingAsync(
        Guid opportunityId,
        string? confirmationNumber,
        bool bindRequestSubmitted,
        string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.Binding,
                "UpdateBindTracking requires stage BINDING");

            opp.BindConfirmationNumber = confirmationNumber?.Trim();
            if (bindRequestSubmitted && !opp.BindRequestSubmittedAt.HasValue)
                opp.BindRequestSubmittedAt = DateTime.UtcNow;

            opp.UpdatedAt = DateTime.UtcNow;

            await WriteActivityAsync(opp.Id, "bind_tracking_updated",
                $"Bind tracking updated — confirmation: {confirmationNumber ?? "none"}",
                actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task RecordBinderReceivedAsync(
        Guid opportunityId,
        DateOnly effectiveDate,
        DateOnly? expirationDate,
        string? policyNumber,
        string? coverageType,
        string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

            Validate(opp.LifecycleStage == LifecycleStage.Binding,
                "RecordBinderReceived requires stage BINDING");

            var winningQuote = opp.Quotes.FirstOrDefault(q => q.IsRecommended);
            var shadow = new PolicyShadowRecord
            {
                OpportunityId       = opportunityId,
                WinningQuoteId      = winningQuote?.Id,
                CarrierName         = winningQuote?.CarrierName,
                PolicyEffectiveDate = effectiveDate,
                ExpirationDate      = expirationDate,
                PolicyNumber        = policyNumber?.Trim(),
                CoverageType        = coverageType?.Trim(),
                PremiumAmount       = winningQuote?.PremiumAmount,
                RenewalTimerStart   = effectiveDate,
                SnapshotJson        = winningQuote?.CoverageDetails,
                BoundAt             = DateTime.UtcNow,
            };
            _db.PolicyShadowRecords.Add(shadow);

            opp.LifecycleStage        = LifecycleStage.Bound;
            opp.LastStageTransitionAt = DateTime.UtcNow;
            opp.UpdatedAt             = DateTime.UtcNow;
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

        // Fire-and-forget: push close to HubSpot after transaction commits
        _ = _hubspot.SyncClosedAsync(opportunityId, reason)
            .ContinueWith(t => {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "[HubSpot] SyncClosed fire-and-forget failed");
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
                CoverageLine  = (coverageTypes != null && !coverageTypes.Contains(','))
                                ? coverageTypes.Trim().ToLowerInvariant()
                                : null,
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
    /// Called at upload time. Creates a fresh Submission row for this upload attempt
    /// and a pending Quote row. Returns the new submission ID.
    /// Each upload gets its own submission + quote row regardless of carrier history.
    /// </summary>
    public async Task<Guid> CreateUploadSubmissionAsync(
        Guid opportunityId,
        string carrierName,
        string? coverageTypes,
        string actorUserId)
    {
        var subId = Guid.Empty;
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var opp = await _db.Opportunities
                .FirstOrDefaultAsync(o => o.Id == opportunityId)
                ?? throw new NotFoundException($"Opportunity {opportunityId} not found");

            // New submission row — unique per upload
            var sub = new Submission
            {
                OpportunityId = opportunityId,
                CarrierName   = carrierName,
                CoverageTypes = coverageTypes,
                CoverageLine  = (coverageTypes != null && !coverageTypes.Contains(','))
                                ? coverageTypes.Trim().ToLowerInvariant()
                                : null,
                Status        = SubmissionStatus.Uploading,
            };
            _db.Submissions.Add(sub);
            await _db.SaveChangesAsync(); // get sub.Id

            // Pending quote row — tied to this specific submission
            _db.Quotes.Add(new Quote
            {
                OpportunityId   = opportunityId,
                SubmissionId    = sub.Id,
                CarrierName     = carrierName,
                PremiumAmount   = 0,
                CoverageDetails = coverageTypes,
                CoverageLine    = sub.CoverageLine,
                IsRecommended   = false,
                ReceivedAt      = DateTime.UtcNow,
                TenantId        = 1,
            });

            await WriteActivityAsync(opportunityId, "submission_created",
                $"Upload started: {carrierName}", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            subId = sub.Id;
        });

        _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} opp={OppId} carrier={Carrier}",
            "UPLOAD_SUBMISSION_CREATED", subId, opportunityId, carrierName);

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

    /// <summary>
    /// Atomically saves scraper result JSON, records the quote, and marks status QuoteReceived.
    /// Combines SaveSubmissionScraperResultAsync + RecordQuoteAsync to avoid nested transaction conflicts.
    /// </summary>
    public async Task SaveScraperResultAndRecordQuoteAsync(
        Guid opportunityId, Guid submissionId, string resultJson,
        decimal? parsedPremium, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var opp = await LoadOpportunityWithDetailsAsync(opportunityId);
            var sub = await _db.Submissions.FindAsync(submissionId)
                ?? throw new NotFoundException($"Submission {submissionId} not found");

            sub.QuoteResultJson = resultJson;
            sub.UpdatedAt = DateTime.UtcNow;

            if (parsedPremium.HasValue && parsedPremium.Value > 0)
            {
                // Record quote in same transaction
                var isFirst = !opp.Quotes.Any(q => q.PremiumAmount > 0);  // first REAL quote

                // Find the pending row (PremiumAmount == 0) for this submission
                // On re-upload, completed quotes (PremiumAmount>0) are preserved; a new row is inserted
                var pendingQuote = await _db.Quotes
                    .FirstOrDefaultAsync(q => q.SubmissionId == submissionId && q.PremiumAmount == 0);

                // Look up LOB for LineOfBusinessId assignment
                Guid? lobId = null;
                if (sub.CoverageLine != null)
                {
                    var lob = await _db.LinesOfBusiness
                        .FirstOrDefaultAsync(l => l.Slug == sub.CoverageLine && l.TenantId == 1);
                    if (lob != null) lobId = lob.Id;
                }

                if (pendingQuote != null)
                {
                    // Update the pending placeholder row with real data
                    _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} quoteId={QuoteId} action=UPDATE premium={Premium}",
                        "DB_WRITE", submissionId, pendingQuote.Id, parsedPremium.Value);
                    pendingQuote.PremiumAmount      = parsedPremium.Value;
                    pendingQuote.CoverageDetails    = sub.CoverageTypes ?? pendingQuote.CoverageDetails;
                    pendingQuote.CoverageLine       = sub.CoverageLine;
                    pendingQuote.LineOfBusinessId   = lobId;
                    pendingQuote.TenantId           = 1;
                }
                else
                {
                    // No pending row (re-upload scenario) — insert a fresh quote row
                    _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} action=INSERT premium={Premium}",
                        "DB_WRITE", submissionId, parsedPremium.Value);
                    _db.Quotes.Add(new Quote
                    {
                        OpportunityId       = opportunityId,
                        SubmissionId        = submissionId,
                        CarrierName         = sub.CarrierName,
                        PremiumAmount       = parsedPremium.Value,
                        CoverageDetails     = sub.CoverageTypes,
                        CoverageLine        = sub.CoverageLine,
                        LineOfBusinessId    = lobId,
                        ReceivedAt          = DateTime.UtcNow,
                        TenantId            = 1,
                    });
                }

                sub.Status = SubmissionStatus.QuoteReceived;
                sub.LineStatus = LineStatus.QuoteReceived;

                // ── Carrier × line routing — mark the correct line row as QuoteReceived ──────
                // Step 1: Try to extract coverage line from scraper result JSON
                string? scrapedLine = null;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
                    // Look for common coverage line keys in scraper output
                    foreach (var key in new[] { "coverage_line", "coverageLine", "line_of_business", "lineOfBusiness", "coverage_type" })
                    {
                        if (doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            scrapedLine = el.GetString();
                            break;
                        }
                    }
                }
                catch { /* malformed JSON — fall through to fallback */ }

                // Find sibling submission rows for the same carrier on this opportunity
                var siblingLines = await _db.Submissions
                    .Where(s => s.OpportunityId == opportunityId
                             && s.CarrierName == sub.CarrierName
                             && s.Id != sub.Id
                             && s.CoverageLine != null)
                    .ToListAsync();

                if (siblingLines.Any())
                {
                    // Multi-line carrier — need to route to correct line row
                    Submission? targetLine = null;

                    // Step 1: Match by scraped line name
                    if (scrapedLine != null)
                    {
                        targetLine = siblingLines.FirstOrDefault(s =>
                            string.Equals(s.CoverageLine, scrapedLine, StringComparison.OrdinalIgnoreCase));
                    }

                    // Step 2: Single sibling shortcut (unambiguous)
                    if (targetLine == null && siblingLines.Count == 1)
                    {
                        targetLine = siblingLines[0];
                    }

                    // Step 3: Fallback — MIN(Id) with warning
                    if (targetLine == null)
                    {
                        targetLine = siblingLines.OrderBy(s => s.Id).First();
                        _logger.LogWarning("[QuoteScraper] {Step} sub={SubId} opp={OppId} carrier={Carrier} scrapedLine={ScrapedLine} action=FALLBACK_MIN_ID targetLine={TargetLine}",
                            "LINE_ROUTING_FALLBACK", submissionId, opportunityId, sub.CarrierName, scrapedLine ?? "null", targetLine.CoverageLine);
                    }

                    if (targetLine.LineStatus != LineStatus.QuoteReceived)
                    {
                        targetLine.LineStatus = LineStatus.QuoteReceived;
                    }
                }
                // If no siblings: sub IS the only line row — sub.LineStatus already set above, nothing more needed

                if (isFirst && opp.LifecycleStage == LifecycleStage.Marketed)
                {
                    var from = opp.LifecycleStage;
                    opp.LifecycleStage = LifecycleStage.QuotesReceived;
                    opp.LastStageTransitionAt = DateTime.UtcNow;
                    opp.UpdatedAt = DateTime.UtcNow;
                    opp.Version++;
                    await WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, new
                    {
                        opportunity_id = opp.Id,
                        from_stage = from.ToString(),
                        to_stage = opp.LifecycleStage.ToString(),
                        actor_user_id = actorUserId,
                        occurred_at = DateTime.UtcNow
                    });
                    await CreateTasksForStageAsync(opp.Id, LifecycleStage.QuotesReceived);
                }

                await RecomputeSignalAsync(opp);
                await WriteActivityAsync(opp.Id, "quote_recorded",
                    $"Quote from {sub.CarrierName}: ${parsedPremium.Value:N0}", actorUserId);
                await WriteOutboxAsync(DomainEventType.QuoteRecorded, new
                {
                    opportunity_id = opportunityId,
                    carrier = sub.CarrierName,
                    premium = parsedPremium.Value,
                    occurred_at = DateTime.UtcNow
                });
            }
            else
            {
                sub.Status = SubmissionStatus.Error;
                sub.ScraperError = "No quote data found in this PDF — the carrier format may not be supported. Click Resubmit to try again or enter manually.";
                _logger.LogError("[QuoteScraper] {Step} sub={SubId} error={Error}",
                    "SET_ERROR", submissionId, sub.ScraperError);
            }

            await WriteActivityAsync(opportunityId, "quote_scraped",
                $"Quote PDF scraped for {sub.CarrierName}", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    /// <summary>
    /// Persist the Fortress API request ID immediately after upload completes.
    /// This makes the scraper job resumable if the user navigates away.
    /// </summary>
    public async Task PersistFortressRequestIdAsync(Guid submissionId, string fortressRequestId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var sub = await _db.Submissions.FindAsync(submissionId)
                ?? throw new NotFoundException($"Submission {submissionId} not found");
            sub.FortressRequestId = fortressRequestId;
            sub.Status = SubmissionStatus.Processing;
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        });
    }

    /// <summary>
    /// Set submission to Error state with the error message.
    /// </summary>
    public async Task SetSubmissionErrorAsync(Guid submissionId, string errorMessage)
    {
        // Sanitize — never persist raw exception messages to user-visible field
        var safeError = "Processing failed — click Resubmit to try again";
        // Log the raw error server-side only
        _logger.LogError("[QuoteScraper] {Step} sub={SubId} error={Error}", "SET_ERROR", submissionId, errorMessage);

        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var sub = await _db.Submissions.FindAsync(submissionId)
                ?? throw new NotFoundException($"Submission {submissionId} not found");
            sub.Status = SubmissionStatus.Error;
            sub.ScraperError = safeError;  // sanitized, not raw errorMessage
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        });
    }

    /// <summary>
    /// Reset a submission's scraper state back to Pending (for retry after error).
    /// </summary>
    public async Task ResetSubmissionScraperAsync(Guid submissionId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var sub = await _db.Submissions.FindAsync(submissionId)
                ?? throw new NotFoundException($"Submission {submissionId} not found");
            sub.Status = SubmissionStatus.Pending;
            sub.FortressRequestId = null;
            sub.ScraperError = null;
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        });
    }

    public async Task DeleteSubmissionAsync(Guid submissionId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var sub = await _db.Submissions.FindAsync(submissionId)
                ?? throw new NotFoundException($"Submission {submissionId} not found");
            _db.Submissions.Remove(sub);
            await WriteActivityAsync(sub.OpportunityId, "submission_deleted",
                $"Submission for {sub.CarrierName} deleted", actorUserId);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task DeleteQuoteAndSubmissionAsync(Guid quoteId, Guid submissionId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var quote = await _db.Quotes.FindAsync(quoteId);
            if (quote != null) _db.Quotes.Remove(quote);

            var sub = await _db.Submissions.FindAsync(submissionId);
            if (sub != null)
            {
                await WriteActivityAsync(sub.OpportunityId, "quote_deleted",
                    $"Quote submission for {sub.CarrierName} deleted", actorUserId);
                _db.Submissions.Remove(sub);
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    /// <summary>Add a contact to an opportunity. Only one Primary allowed per opportunity.</summary>
    public async Task<Guid> AddContactAsync(
        Guid opportunityId, string firstName, string lastName,
        string? title, string? email, string? phone,
        FamOs.Web.Data.Entities.ContactType contactType, string? notes, string actorUserId)
    {
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);
            Validate(!opp.IsClosed, "Cannot add contacts to a closed opportunity");

            if (contactType == FamOs.Web.Data.Entities.ContactType.Primary
                && opp.Contacts.Any(c => c.ContactType == FamOs.Web.Data.Entities.ContactType.Primary))
            {
                throw new LifecycleValidationException(
                    "This opportunity already has a primary contact. " +
                    "Update the existing primary contact or use a different contact type.");
            }

            var contact = new FamOs.Web.Data.Entities.Contact
            {
                OpportunityId = opportunityId,
                FirstName     = firstName.Trim(),
                LastName      = lastName.Trim(),
                Title         = title?.Trim(),
                Email         = email?.Trim(),
                Phone         = phone?.Trim(),
                ContactType   = contactType,
                Notes         = notes?.Trim(),
            };
            _db.Contacts.Add(contact);

            if (contactType == FamOs.Web.Data.Entities.ContactType.Primary)
                opp.PrimaryContactId = contact.Id;

            opp.UpdatedAt = DateTime.UtcNow;
            await WriteActivityAsync(opp.Id, "contact_added",
                $"Contact added: {firstName} {lastName} ({contactType})", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return contact.Id;
        });
    }

    /// <summary>Remove a contact from an opportunity.</summary>
    public async Task RemoveContactAsync(Guid contactId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var contact = await _db.Contacts
                .Include(c => c.Opportunity)
                .FirstOrDefaultAsync(c => c.Id == contactId)
                ?? throw new NotFoundException($"Contact {contactId} not found");

            var opp = contact.Opportunity;
            _db.Contacts.Remove(contact);

            if (opp.PrimaryContactId == contactId)
                opp.PrimaryContactId = null;

            opp.UpdatedAt = DateTime.UtcNow;
            await WriteActivityAsync(opp.Id, "contact_removed",
                $"Contact removed: {contact.FullName}", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    /// <summary>Assign a new owner to an opportunity.</summary>
    public async Task AssignOwnerAsync(Guid opportunityId, string newOwnerUserId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);
            Validate(!opp.IsClosed, "Cannot reassign owner of a closed opportunity");

            var previous      = opp.OwnerUserId;
            opp.OwnerUserId   = newOwnerUserId;
            opp.UpdatedAt     = DateTime.UtcNow;

            await WriteActivityAsync(opp.Id, "owner_assigned",
                $"Owner changed from {previous} to {newOwnerUserId}", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });

        // Fire-and-forget: push owner change to HubSpot after transaction commits
        _ = _hubspot.SyncOwnerAsync(opportunityId, newOwnerUserId)
            .ContinueWith(t => {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "[HubSpot] SyncOwner fire-and-forget failed");
            });
    }

    /// <summary>Adds a manual note to the opportunity activity log.</summary>
    public async Task AddNoteAsync(Guid opportunityId, string noteText, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var opp = await LoadOpportunityAsync(opportunityId);
            Validate(!opp.IsClosed, "Cannot add notes to a closed opportunity");
            Validate(!string.IsNullOrWhiteSpace(noteText), "Note text cannot be empty");

            await WriteActivityAsync(opp.Id, "note", noteText.Trim(), actorUserId);
            opp.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }

    public async Task<List<LineOfBusiness>> GetLinesOfBusinessAsync(int tenantId = 1)
    {
        return await _db.LinesOfBusiness
            .Where(l => l.TenantId == tenantId && l.IsActive)
            .OrderBy(l => l.DisplayOrder)
            .ToListAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Opportunity> LoadOpportunityAsync(Guid id)
    {
        var opp = await _db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Contacts)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new NotFoundException($"Opportunity {id} not found");
        return opp;
    }

    private async Task<Opportunity> LoadOpportunityWithDetailsAsync(Guid id)
    {
        var opp = await _db.Opportunities
            .Include(o => o.Submissions)
            .Include(o => o.Quotes)
            .Include(o => o.Contacts)
            .Include(o => o.Proposals)
            .Include(o => o.Tasks.Where(t => t.Status == "open"))
            .Include(o => o.Flags)
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

    /// <summary>
    /// Derives carrier-level status from its line statuses.
    /// </summary>
    public static LineStatusSummary GetCarrierStatus(IEnumerable<Submission> lines)
    {
        var lineList = lines.ToList();
        if (!lineList.Any()) return LineStatusSummary.Pending;
        if (lineList.All(l => l.LineStatus == LineStatus.Declined)) return LineStatusSummary.Declined;
        if (lineList.All(l => l.LineStatus == LineStatus.QuoteReceived || l.LineStatus == LineStatus.Declined))
            return LineStatusSummary.Complete;
        return LineStatusSummary.Pending;
    }

    /// <summary>
    /// Marks a submission line as declined (carrier declined this coverage line).
    /// </summary>
    public async Task MarkLineDeclinedAsync(Guid submissionId, string actorUserId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var sub = await _db.Submissions.FindAsync(submissionId)
                ?? throw new NotFoundException($"Submission {submissionId} not found");

            sub.LineStatus = LineStatus.Declined;
            sub.UpdatedAt = DateTime.UtcNow;

            await WriteActivityAsync(sub.OpportunityId, "line_declined",
                $"{sub.CarrierName} × {sub.CoverageLine} marked declined", actorUserId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });
    }
}

public enum LineStatusSummary
{
    Pending,
    Complete,
    Declined
}

public class LifecycleValidationException : Exception
{
    public LifecycleValidationException(string message) : base(message) { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
