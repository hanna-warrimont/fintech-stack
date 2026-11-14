using Portal.Instruments.Domain.CorporateActions;
using Portal.Instruments.Domain.Instruments;

namespace Portal.Tests.Instruments;

public sealed class CorporateActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(1);
    private static readonly DateTimeOffset Due = Now.AddMinutes(5);

    private static Instrument Sap() =>
        Instrument.Create(Isin.Create("DE0007164600").Value, null, "SAP SE", InstrumentType.Share, Currency.Create("EUR").Value).Value;

    private static CorporateAction Split(Instrument? instrument = null, DateTimeOffset? effectiveAt = null) =>
        CorporateAction.CreateSplit(instrument ?? Sap(), SplitRatio.Create(1, 4).Value, effectiveAt ?? Due, "pia").Value;

    private static CorporateAction Approved(Instrument? instrument = null, CorporateAction? action = null)
    {
        action ??= Split(instrument);
        action.Submit();
        action.Approve("fred", Now, LeadTime);
        return action;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(1, 1001)]
    public void Split_ratio_must_be_positive_and_different(int old, int @new) =>
        Assert.Equal(CorporateActionErrors.InvalidSplitRatio, SplitRatio.Create(old, @new).Error);

    [Fact]
    public void Split_factor_is_old_over_new() => Assert.Equal(0.25m, SplitRatio.Create(1, 4).Value.Factor);

    [Fact]
    public void New_action_is_draft_created_by_user()
    {
        var action = Split();

        Assert.Equal(CorporateActionStatus.Draft, action.Status);
        Assert.Equal("pia", action.CreatedBy);
        Assert.Equal(1, action.Revision);
        Assert.Empty(action.DomainEvents);
    }

    [Fact]
    public void Delisted_instrument_takes_no_new_action()
    {
        var instrument = Sap();
        instrument.Delist();

        Assert.Equal(CorporateActionErrors.InstrumentDelisted,
            CorporateAction.CreateDelisting(instrument, Due, "pia").Error);
    }

    [Fact]
    public void Isin_change_to_same_isin_is_rejected()
    {
        var instrument = Sap();

        Assert.Equal(CorporateActionErrors.SameIsin,
            CorporateAction.CreateIsinChange(instrument, instrument.Isin, Due, "pia").Error);
    }

    [Fact]
    public void Happy_path_submit_approve()
    {
        var action = Split();

        Assert.True(action.Submit().IsSuccess);
        Assert.True(action.Approve("fred", Now, LeadTime).IsSuccess);

        Assert.Equal(CorporateActionStatus.Approved, action.Status);
        Assert.Equal("fred", action.DecidedBy);
        Assert.Equal(3, action.Revision);
        Assert.Equal([new CorporateActionApproved(action.Id, action.InstrumentId)], action.DomainEvents);
    }

    [Fact]
    public void Approve_by_creator_is_rejected()
    {
        var action = Split();
        action.Submit();

        Assert.Equal(CorporateActionErrors.FourEyes, action.Approve("pia", Now, LeadTime).Error);
        Assert.Equal(CorporateActionStatus.Submitted, action.Status);
    }

    [Fact]
    public void Approve_with_effective_at_inside_lead_time_is_rejected()
    {
        var action = Split(effectiveAt: Now.AddSeconds(30));
        action.Submit();

        Assert.Equal(CorporateActionErrors.LeadTimeTooShort, action.Approve("fred", Now, LeadTime).Error);
    }

    [Fact]
    public void Approve_from_draft_is_invalid_transition() =>
        Assert.Equal(CorporateActionErrors.InvalidTransition, Split().Approve("fred", Now, LeadTime).Error);

    [Fact]
    public void Withdraw_returns_to_draft()
    {
        var action = Split();
        action.Submit();

        Assert.True(action.Withdraw().IsSuccess);
        Assert.Equal(CorporateActionStatus.Draft, action.Status);
    }

    [Fact]
    public void Discard_only_from_draft()
    {
        var action = Split();
        action.Submit();
        Assert.Equal(CorporateActionErrors.InvalidTransition, action.Discard().Error);

        action.Withdraw();
        Assert.True(action.Discard().IsSuccess);
        Assert.Equal(CorporateActionStatus.Discarded, action.Status);
    }

    [Fact]
    public void Reject_from_submitted()
    {
        var action = Split();
        action.Submit();

        Assert.True(action.Reject("fred").IsSuccess);
        Assert.Equal(CorporateActionStatus.Rejected, action.Status);
        Assert.Equal("fred", action.DecidedBy);
        Assert.Equal([new CorporateActionRejected(action.Id, action.InstrumentId)], action.DomainEvents);
    }

    [Fact]
    public void Cancel_before_effective_at_is_allowed()
    {
        var action = Approved();

        Assert.True(action.Cancel("fred", Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(CorporateActionStatus.Cancelled, action.Status);
        Assert.Contains(new CorporateActionCancelled(action.Id, action.InstrumentId), action.DomainEvents);
    }

    [Fact]
    public void Cancel_after_effective_at_is_rejected() =>
        Assert.Equal(CorporateActionErrors.AlreadyEffectiveDue, Approved().Cancel("fred", Due).Error);

    [Fact]
    public void Become_effective_before_due_is_rejected()
    {
        var instrument = Sap();

        Assert.Equal(CorporateActionErrors.InvalidTransition,
            Approved(instrument).BecomeEffective(instrument, Due.AddSeconds(-1)).Error);
    }

    [Fact]
    public void Become_effective_applies_isin_change_to_instrument()
    {
        var instrument = Sap();
        var newIsin = Isin.Create("US0378331005").Value;
        var action = Approved(instrument, CorporateAction.CreateIsinChange(instrument, newIsin, Due, "pia").Value);

        Assert.True(action.BecomeEffective(instrument, Due).IsSuccess);

        Assert.Equal(CorporateActionStatus.Effective, action.Status);
        Assert.Equal(newIsin, instrument.Isin);
        Assert.Equal(2, instrument.Revision);
        Assert.Contains(new CorporateActionBecameEffective(action.Id, action.InstrumentId), action.DomainEvents);
    }

    [Fact]
    public void Become_effective_applies_delisting()
    {
        var instrument = Sap();
        var action = Approved(instrument, CorporateAction.CreateDelisting(instrument, Due, "pia").Value);

        action.BecomeEffective(instrument, Due);

        Assert.Equal(InstrumentStatus.Delisted, instrument.Status);
    }

    [Fact]
    public void Become_effective_split_leaves_instrument_unchanged()
    {
        var instrument = Sap();
        var action = Approved(instrument);

        Assert.True(action.BecomeEffective(instrument, Due).IsSuccess);

        Assert.Equal(1, instrument.Revision);
        Assert.Equal(CorporateActionStatus.Effective, action.Status);
    }
}
