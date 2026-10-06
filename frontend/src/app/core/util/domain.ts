import {
  ClaimStatus, Policy, ReserveApprovalStatus, ReserveComponentType, RoleCode, RoleName,
} from '../models/api.models';

// ---------------------------------------------------------------- reserve authority (FRS §6.3)

export const AUTO_APPROVAL_LIMIT = 10_000;
export const SUPERVISOR_LIMIT = 100_000;
export const MANAGER_LIMIT = 10_000_000;

export type AuthorityLevel = 'auto' | 'supervisor' | 'manager';

/** Which authority a transaction of this size needs. The size, not the running total, decides. */
export function requiredAuthority(amount: number): AuthorityLevel {
  const size = Math.abs(amount);
  if (size <= AUTO_APPROVAL_LIMIT) return 'auto';
  return size <= SUPERVISOR_LIMIT ? 'supervisor' : 'manager';
}

export function authorityMessage(level: AuthorityLevel): string {
  switch (level) {
    case 'auto': return '✓ Auto-approved (≤ $10,000)';
    case 'supervisor': return '⚠ Supervisor approval required';
    case 'manager': return '⚠ Manager approval required';
  }
}

const ROLE_RANK: Record<RoleCode, number> = { handler: 1, supervisor: 2, manager: 3 };
const ROLE_NAME_RANK: Record<RoleName, number> = { Handler: 1, Supervisor: 2, Manager: 3 };

export function roleRank(role: RoleCode | null | undefined): number {
  return role ? ROLE_RANK[role] : 0;
}

export function meetsMinimumRole(role: RoleCode | null | undefined, minimum: RoleName): boolean {
  return roleRank(role) >= ROLE_NAME_RANK[minimum];
}

export function canApproveAmount(role: RoleCode | null | undefined, amount: number): boolean {
  switch (requiredAuthority(amount)) {
    case 'auto': return true;
    case 'supervisor': return roleRank(role) >= 2 && Math.abs(amount) <= MANAGER_LIMIT;
    case 'manager': return roleRank(role) >= 3 && Math.abs(amount) <= MANAGER_LIMIT;
  }
}

/** SubrogationRecoverable is the only component whose amount may be negative (FRS §6.2). */
export function allowsNegative(component: ReserveComponentType): boolean {
  return component === 'SubrogationRecoverable';
}

export function isValidReserveAmount(component: ReserveComponentType, amount: number | null | undefined): boolean {
  if (amount === null || amount === undefined || Number.isNaN(amount)) return false;
  return allowsNegative(component) ? amount !== 0 : amount > 0;
}

// ---------------------------------------------------------------- policy cover indicator (FRS §11.2)

export type PolicyCover = {
  state: 'unknown' | 'in-force' | 'attention';
  label: string;
};

const toUtcDay = (iso: string): string => new Date(iso).toISOString().slice(0, 10);

/**
 * Green when the loss date falls inside the policy period, amber when the policy is expired or the date is outside
 * the window. Comparison is by UTC calendar day, exactly like the API's warning rule.
 */
export function policyCover(policy: Policy | null, lossDateIso: string | null): PolicyCover {
  if (!policy) return { state: 'unknown', label: 'No policy selected' };
  if (!lossDateIso) return { state: 'unknown', label: `Policy ${policy.status.toLowerCase()} · enter a loss date to check cover` };

  const day = toUtcDay(lossDateIso);
  if (day < policy.effectiveDate || day > policy.expirationDate) {
    return { state: 'attention', label: 'Loss date outside the policy period' };
  }

  return policy.status === 'Active'
    ? { state: 'in-force', label: 'In force on the loss date' }
    : { state: 'attention', label: `Policy ${policy.status.toLowerCase()} (loss date is within its period)` };
}

// ---------------------------------------------------------------- display helpers

export const STATUS_LABELS: Record<ClaimStatus, string> = {
  Draft: 'Draft',
  Open: 'Open',
  UnderInvestigation: 'Under investigation',
  PendingPayment: 'Pending payment',
  Closed: 'Closed',
  Reopened: 'Reopened',
  Withdrawn: 'Withdrawn',
};

export const ALL_STATUSES = Object.keys(STATUS_LABELS) as ClaimStatus[];

export function isPendingApproval(status: ReserveApprovalStatus): boolean {
  return status === 'PendingApproval';
}

/** True for transitions that must be accompanied by a reason (withdrawal, reopening). */
export function transitionNeedsReason(target: ClaimStatus): boolean {
  return target === 'Withdrawn' || target === 'Reopened';
}

export function splitCamelCase(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
