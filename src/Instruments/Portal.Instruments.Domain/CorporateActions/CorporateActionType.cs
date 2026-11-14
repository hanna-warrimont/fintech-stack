namespace Portal.Instruments.Domain.CorporateActions;

public enum CorporateActionType { Split, IsinChange, Delisting }

public enum CorporateActionStatus { Draft, Submitted, Approved, Rejected, Cancelled, Discarded, Effective }
