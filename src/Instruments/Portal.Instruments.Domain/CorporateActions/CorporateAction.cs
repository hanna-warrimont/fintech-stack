using Portal.Instruments.Domain.Abstractions;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Instruments.Domain.CorporateActions;

/// <summary>
/// Aggregat Kapitalmaßnahme (Split, ISIN-Wechsel, Delisting) mit Zustandsautomat:
/// <code>
/// Draft ──Submit──▶ Submitted ──Approve──▶ Approved ──Stichtag (BecomeEffective)──▶ Effective
///   │                  │  └──Reject──▶ Rejected
///   └──Discard         └──Withdraw──▶ Draft
/// Approved ──Cancel (vor dem Stichtag)──▶ Cancelled
/// </code>
/// Regeln: Vier-Augen (wer erfasst, gibt nicht frei), Stichtag mit Mindestvorlauf bei der Freigabe,
/// keine Maßnahme für delistete Instrumente. Jeder Übergang erhöht <see cref="Revision"/> (Optimistic Concurrency).
/// </summary>
public sealed class CorporateAction : Entity
{
    /// <summary>Entscheider, wenn der Wirksamkeits-Job eine Maßnahme verwirft.</summary>
    public const string SystemUser = "system";

    private CorporateAction() { } // für EF

    public Guid Id { get; private set; }
    public Guid InstrumentId { get; private set; }
    public CorporateActionType Type { get; private set; }
    public SplitRatio? Ratio { get; private set; }
    public Isin? NewIsin { get; private set; }
    public DateTimeOffset EffectiveAt { get; private set; }
    public CorporateActionStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public string? DecidedBy { get; private set; }
    public int Revision { get; private set; }

    public static Result<CorporateAction> CreateSplit(Instrument instrument, SplitRatio ratio, DateTimeOffset effectiveAt, string user) =>
        Create(instrument, CorporateActionType.Split, ratio, null, effectiveAt, user);

    public static Result<CorporateAction> CreateIsinChange(Instrument instrument, Isin newIsin, DateTimeOffset effectiveAt, string user) =>
        newIsin == instrument.Isin
            ? CorporateActionErrors.SameIsin
            : Create(instrument, CorporateActionType.IsinChange, null, newIsin, effectiveAt, user);

    public static Result<CorporateAction> CreateDelisting(Instrument instrument, DateTimeOffset effectiveAt, string user) =>
        Create(instrument, CorporateActionType.Delisting, null, null, effectiveAt, user);

    private static Result<CorporateAction> Create(
        Instrument instrument, CorporateActionType type, SplitRatio? ratio, Isin? newIsin, DateTimeOffset effectiveAt, string user)
    {
        if (instrument.Status == InstrumentStatus.Delisted) return CorporateActionErrors.InstrumentDelisted;

        return new CorporateAction
        {
            Id = Guid.CreateVersion7(),
            InstrumentId = instrument.Id,
            Type = type,
            Ratio = ratio,
            NewIsin = newIsin,
            EffectiveAt = effectiveAt,
            Status = CorporateActionStatus.Draft,
            CreatedBy = user,
            Revision = 1,
        };
    }

    public Result Submit() => Transition(CorporateActionStatus.Submitted, CorporateActionStatus.Draft);

    public Result Withdraw() => Transition(CorporateActionStatus.Draft, CorporateActionStatus.Submitted);

    public Result Discard() => Transition(CorporateActionStatus.Discarded, CorporateActionStatus.Draft);

    public Result Approve(string user, DateTimeOffset now, TimeSpan leadTime)
    {
        if (Status != CorporateActionStatus.Submitted) return Result.Failure(CorporateActionErrors.InvalidTransition);
        if (user == CreatedBy) return Result.Failure(CorporateActionErrors.FourEyes);
        if (EffectiveAt < now + leadTime) return Result.Failure(CorporateActionErrors.LeadTimeTooShort);

        return Decide(CorporateActionStatus.Approved, user, new CorporateActionApproved(Id, InstrumentId));
    }

    public Result Reject(string user) => Status == CorporateActionStatus.Submitted
        ? Decide(CorporateActionStatus.Rejected, user, new CorporateActionRejected(Id, InstrumentId))
        : Result.Failure(CorporateActionErrors.InvalidTransition);

    public Result Cancel(string user, DateTimeOffset now)
    {
        if (Status != CorporateActionStatus.Approved) return Result.Failure(CorporateActionErrors.InvalidTransition);
        if (now >= EffectiveAt) return Result.Failure(CorporateActionErrors.AlreadyEffectiveDue);

        return Decide(CorporateActionStatus.Cancelled, user, new CorporateActionCancelled(Id, InstrumentId));
    }

    /// <summary>Vom Wirksamkeits-Job aufgerufen: wendet die Maßnahme auf das Instrument an (Split ändert am Instrument nichts).</summary>
    public Result BecomeEffective(Instrument instrument, DateTimeOffset now)
    {
        if (Status != CorporateActionStatus.Approved || now < EffectiveAt || instrument.Id != InstrumentId)
            return Result.Failure(CorporateActionErrors.InvalidTransition);

        var applied = Type switch
        {
            CorporateActionType.IsinChange => instrument.ChangeIsin(NewIsin!),
            CorporateActionType.Delisting => instrument.Delist(),
            _ => Result.Success(),
        };
        if (applied.IsFailure) return applied;

        Transition(CorporateActionStatus.Effective, CorporateActionStatus.Approved);
        Raise(new CorporateActionBecameEffective(Id, InstrumentId));
        return Result.Success();
    }

    /// <summary>
    /// Zum Stichtag nicht anwendbar (Instrument inzwischen delistet, neue ISIN inzwischen vergeben): Die Maßnahme
    /// endet als storniert, statt bei jedem Job-Lauf erneut zu scheitern und andere Maßnahmen zu blockieren.
    /// </summary>
    public Result CancelAsInapplicable() => Status == CorporateActionStatus.Approved
        ? Decide(CorporateActionStatus.Cancelled, SystemUser, new CorporateActionCancelled(Id, InstrumentId))
        : Result.Failure(CorporateActionErrors.InvalidTransition);

    private Result Decide(CorporateActionStatus to, string user, IDomainEvent @event)
    {
        var result = Transition(to, Status);
        DecidedBy = user;
        Raise(@event);
        return result;
    }

    private Result Transition(CorporateActionStatus to, CorporateActionStatus from)
    {
        if (Status != from) return Result.Failure(CorporateActionErrors.InvalidTransition);
        Status = to;
        Revision++;
        return Result.Success();
    }
}
