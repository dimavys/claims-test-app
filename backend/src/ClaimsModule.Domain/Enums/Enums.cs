namespace ClaimsModule.Domain.Enums;

public enum ClaimStatus { Draft, Open, UnderInvestigation, PendingPayment, Closed, Reopened, Withdrawn }

public enum ClaimSeverity { Minor, Standard, Critical, Catastrophic }

/// <summary>Ordered by authority: a higher value includes every capability of the lower ones.</summary>
public enum UserRole { Handler = 1, Supervisor = 2, Manager = 3 }

public enum PartyRole { Claimant, Insured, ThirdParty, Witness, Attorney }

public enum PartyType { Person, Company }

public enum AssetType { Vehicle, Property, Person, Equipment, Other }

public enum ReserveComponentType { Indemnity, Expense, ALAE, SubrogationRecoverable }

public enum ReserveComponentStatus { Active, Closed }

public enum ReserveTransactionType { Add, Adjust, Reverse }

public enum ReserveApprovalStatus { AutoApproved, PendingApproval, Approved, Rejected, Cancelled }

public enum ReservePostingStatus { Pending, Posted, Failed, Cancelled }

/// <summary>Who may approve a transaction of a given size (FRS §6.3).</summary>
public enum AuthorityLevel { Auto, Supervisor, Manager }

public enum ValidationSeverity { Warning, Critical }

public enum PolicyStatus { Active, Expired, Cancelled }

public enum PerilCategory { Property, Auto, Liability, Weather, Equipment, Crime, General }
