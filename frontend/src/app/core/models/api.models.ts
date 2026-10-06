// Types mirroring the API contracts (enums are serialised as strings).

export type ClaimStatus =
  | 'Draft' | 'Open' | 'UnderInvestigation' | 'PendingPayment' | 'Closed' | 'Reopened' | 'Withdrawn';
export type ClaimSeverity = 'Minor' | 'Standard' | 'Critical' | 'Catastrophic';
/** Role names as returned inside DTOs (enum names). Tokens use the lower-case codes in `RoleCode`. */
export type RoleName = 'Handler' | 'Supervisor' | 'Manager';
export type RoleCode = 'handler' | 'supervisor' | 'manager';
export type PartyRole = 'Claimant' | 'Insured' | 'ThirdParty' | 'Witness' | 'Attorney';
export type PartyType = 'Person' | 'Company';
export type AssetType = 'Vehicle' | 'Property' | 'Person' | 'Equipment' | 'Other';
export type ReserveComponentType = 'Indemnity' | 'Expense' | 'ALAE' | 'SubrogationRecoverable';
export type ReserveTransactionType = 'Add' | 'Adjust' | 'Reverse';
export type ReserveApprovalStatus = 'AutoApproved' | 'PendingApproval' | 'Approved' | 'Rejected' | 'Cancelled';
export type ReservePostingStatus = 'Pending' | 'Posted' | 'Failed' | 'Cancelled';
export type ValidationSeverity = 'Warning' | 'Critical';
export type PolicyStatus = 'Active' | 'Expired' | 'Cancelled';
export type PerilCategory = 'Property' | 'Auto' | 'Liability' | 'Weather' | 'Equipment' | 'Crime' | 'General';

export interface Paged<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ClaimSummary {
  id: string;
  claimNumber: string;
  clientName: string;
  policyNumber: string | null;
  lossDate: string;
  causeOfLossCode: string;
  causeOfLossName: string | null;
  status: ClaimStatus;
  totalReserves: number;
  assignedHandlerId: string | null;
  assignedHandlerName: string | null;
  reportedDate: string;
}

export interface LossEvent {
  id: string;
  lossDate: string;
  lossDescription: string;
  lossLocation: string | null;
  causeOfLossCode: string;
  causeOfLossName: string | null;
  estimatedLossAmount: number | null;
  reportDate: string;
  policeReportNumber: string | null;
}

export interface ClaimParty {
  id: string;
  partyRole: PartyRole;
  partyType: PartyType;
  displayName: string;
  firstName: string | null;
  lastName: string | null;
  companyName: string | null;
  email: string | null;
  phone: string | null;
  notes: string | null;
  isActive: boolean;
}

export interface RiskObject {
  id: string;
  assetType: AssetType;
  assetDescription: string;
  damageDescription: string | null;
  isPrimary: boolean;
  assetReference: string | null;
}

export interface ValidationIssue {
  id: string;
  severity: ValidationSeverity;
  code: string;
  field: string | null;
  message: string;
  requiresAcknowledgement: boolean;
  isResolved: boolean;
  isAcknowledged: boolean;
  isOutstanding: boolean;
}

export interface ClaimDocument {
  id: string;
  documentType: string;
  documentName: string;
  contentType: string;
  fileSizeBytes: number;
  uploadedAt: string;
  uploadedByUserId: string | null;
  uploadedByName: string | null;
  notes: string | null;
  downloadUrl?: string | null;
  downloadUrlExpiresAt?: string | null;
}

export interface AuditEntry {
  id: string;
  eventType: string;
  description: string;
  oldValue: string | null;
  newValue: string | null;
  relatedEntityId: string | null;
  relatedEntityType: string | null;
  correlationId: string | null;
  createdAt: string;
  createdByUserId: string | null;
  createdByName: string | null;
}

export interface NextStatus {
  status: ClaimStatus;
  minimumRole: RoleName;
  conditions: string;
}

export interface ReserveComponent {
  id: string;
  component: ReserveComponentType;
  currentAmount: number;
  pendingAmount: number;
  status: 'Active' | 'Closed';
}

export interface ReserveSummary {
  components: ReserveComponent[];
  totalReserves: number;
  totalPending: number;
  managerOverride: boolean;
}

export interface ReserveTransaction {
  id: string;
  reserveComponentId: string;
  component: ReserveComponentType;
  transactionType: ReserveTransactionType;
  amount: number;
  previousBalance: number;
  newBalance: number;
  approvalStatus: ReserveApprovalStatus;
  postingStatus: ReservePostingStatus;
  postingJobId: string | null;
  changeReason: string;
  changeSequence: number;
  idempotencyKey: string;
  submittedByUserId: string | null;
  submittedByName: string | null;
  approvedByUserId: string | null;
  approvedByName: string | null;
  approvedAt: string | null;
  rejectedByUserId: string | null;
  rejectedAt: string | null;
  rejectionReason: string | null;
  createdAt: string;
}

export interface ReserveSubmission {
  transaction: ReserveTransaction;
  warnings: string[];
  requiredAuthority: string;
  requiresApproval: boolean;
}

export interface Reserves {
  summary: ReserveSummary;
  transactions: ReserveTransaction[];
}

export interface ClaimDetail {
  id: string;
  claimNumber: string;
  policyId: string | null;
  policyNumber: string | null;
  clientName: string;
  status: ClaimStatus;
  severity: ClaimSeverity;
  claimType: string | null;
  reportedDate: string;
  assignedHandlerId: string | null;
  assignedHandlerName: string | null;
  closedAt: string | null;
  closureReason: string | null;
  notes: string | null;
  managerOverride: boolean;
  lossEvent: LossEvent;
  parties: ClaimParty[];
  riskObjects: RiskObject[];
  validationIssues: ValidationIssue[];
  documents: ClaimDocument[];
  reserveSummary: ReserveSummary;
  recentAudit: AuditEntry[];
  validNextStatuses: NextStatus[];
}

export interface ClaimCreated {
  id: string;
  claimNumber: string;
  status: ClaimStatus;
  validationIssues: ValidationIssue[];
  initialReserve: ReserveSubmission | null;
}

export interface ClaimStatusChanged {
  id: string;
  previousStatus: ClaimStatus;
  status: ClaimStatus;
  validNextStatuses: NextStatus[];
}

export interface ClosurePreflight {
  canClose: boolean;
  blockers: string[];
  openReserveBalance: number;
  requiresJustification: boolean;
}

export interface ValidationReport {
  critical: string[];
  warnings: string[];
  isValid: boolean;
}

export interface Policy {
  id: string;
  policyNumber: string;
  clientName: string;
  effectiveDate: string;
  expirationDate: string;
  status: PolicyStatus;
  coverageTypes: string[];
}

export interface CauseOfLossCode {
  code: string;
  name: string;
  perilCategory: PerilCategory;
}

export interface ClaimStatusInfo {
  status: ClaimStatus;
  validNextStatuses: NextStatus[];
}

// ---------------------------------------------------------------- requests

export interface PartyInput {
  partyRole: PartyRole;
  partyType: PartyType;
  firstName?: string | null;
  lastName?: string | null;
  companyName?: string | null;
  email?: string | null;
  phone?: string | null;
  notes?: string | null;
}

export interface RiskObjectInput {
  assetType: AssetType;
  assetDescription: string;
  damageDescription?: string | null;
  assetReference?: string | null;
}

export interface InitialReserveInput {
  component: ReserveComponentType;
  amount: number;
  changeReason?: string | null;
}

export interface CreateClaimRequest {
  policyId: string | null;
  lossDate: string;
  lossDescription: string;
  causeOfLossCode: string;
  lossLocation?: string | null;
  estimatedLossAmount?: number | null;
  parties: PartyInput[];
  riskObjects: RiskObjectInput[];
  initialReserve?: InitialReserveInput | null;
}

export interface ClaimListFilters {
  status?: ClaimStatus[];
  dateFrom?: string | null;
  dateTo?: string | null;
  assignedHandler?: string | null;
  causeOfLossCode?: string | null;
  search?: string | null;
  page: number;
  pageSize: number;
}

export interface TransitionRequest {
  targetStatus: ClaimStatus;
  reason?: string | null;
  acknowledgeWarnings?: boolean;
  closureJustification?: string | null;
}

export interface SubmitReserveRequest {
  component: ReserveComponentType;
  amount: number;
  changeReason: string;
  transactionType: ReserveTransactionType;
}

// ---------------------------------------------------------------- auth & errors

export interface UserInfo {
  id: string;
  userName: string;
  displayName: string;
  role: RoleCode;
}

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  user: UserInfo;
}

/** The API's single error shape (FRS §10.4). */
export interface ApiError {
  type: string;
  title: string;
  status: number;
  detail?: string;
  errors?: Record<string, string[]>;
  correlationId?: string;
  validNextStatuses?: ClaimStatus[];
  blockingConditions?: string[];
}
