import { describe, expect, it } from 'vitest';
import { Policy } from '../models/api.models';
import {
  authorityMessage, canApproveAmount, isValidReserveAmount, meetsMinimumRole, policyCover, requiredAuthority,
  roleRank, splitCamelCase, transitionNeedsReason,
} from './domain';

describe('requiredAuthority', () => {
  it.each([
    [0.01, 'auto'], [10_000, 'auto'], [10_000.01, 'supervisor'], [100_000, 'supervisor'],
    [100_000.01, 'manager'], [10_000_000, 'manager'], [-8_000, 'auto'], [-50_000, 'supervisor'],
  ])('amount %s needs %s authority', (amount, expected) => {
    expect(requiredAuthority(amount)).toBe(expected);
  });

  it('describes each level the way the form shows it', () => {
    expect(authorityMessage('auto')).toBe('✓ Auto-approved (≤ $10,000)');
    expect(authorityMessage('supervisor')).toBe('⚠ Supervisor approval required');
    expect(authorityMessage('manager')).toBe('⚠ Manager approval required');
  });
});

describe('canApproveAmount', () => {
  it.each([
    ['handler', 10_000, true], ['handler', 10_001, false],
    ['supervisor', 100_000, true], ['supervisor', 100_001, false],
    ['manager', 100_001, true], ['manager', 10_000_000, true], ['manager', 10_000_001, false],
  ] as const)('%s on %s → %s', (role, amount, expected) => {
    expect(canApproveAmount(role, amount)).toBe(expected);
  });

  it('is false when signed out', () => {
    expect(canApproveAmount(null, 20_000)).toBe(false);
  });
});

describe('roles', () => {
  it('ranks handler < supervisor < manager', () => {
    expect(roleRank('handler')).toBeLessThan(roleRank('supervisor'));
    expect(roleRank('supervisor')).toBeLessThan(roleRank('manager'));
    expect(roleRank(null)).toBe(0);
  });

  it('compares a role code with a DTO role name', () => {
    expect(meetsMinimumRole('supervisor', 'Handler')).toBe(true);
    expect(meetsMinimumRole('handler', 'Supervisor')).toBe(false);
    expect(meetsMinimumRole('manager', 'Supervisor')).toBe(true);
  });
});

describe('isValidReserveAmount', () => {
  it('requires a positive amount for ordinary components', () => {
    expect(isValidReserveAmount('Indemnity', 1)).toBe(true);
    expect(isValidReserveAmount('Indemnity', 0)).toBe(false);
    expect(isValidReserveAmount('Expense', -5)).toBe(false);
    expect(isValidReserveAmount('ALAE', null)).toBe(false);
    expect(isValidReserveAmount('ALAE', Number.NaN)).toBe(false);
  });

  it('allows negative but not zero for subrogation recoverable', () => {
    expect(isValidReserveAmount('SubrogationRecoverable', -8_000)).toBe(true);
    expect(isValidReserveAmount('SubrogationRecoverable', 8_000)).toBe(true);
    expect(isValidReserveAmount('SubrogationRecoverable', 0)).toBe(false);
  });
});

describe('policyCover', () => {
  const policy = (over: Partial<Policy> = {}): Policy => ({
    id: '1', policyNumber: 'POL-1', clientName: 'Acme', effectiveDate: '2024-01-01', expirationDate: '2026-12-31',
    status: 'Active', coverageTypes: ['Vehicle'], ...over,
  });

  it('is unknown without a policy or a loss date', () => {
    expect(policyCover(null, '2025-01-01T00:00:00Z').state).toBe('unknown');
    expect(policyCover(policy(), null).state).toBe('unknown');
  });

  it('is green when an active policy covers the loss date, boundaries included', () => {
    expect(policyCover(policy(), '2025-06-01T12:00:00Z').state).toBe('in-force');
    expect(policyCover(policy(), '2024-01-01T00:00:00Z').state).toBe('in-force');
    expect(policyCover(policy(), '2026-12-31T23:59:00Z').state).toBe('in-force');
  });

  it('is amber when the loss date is outside the period', () => {
    expect(policyCover(policy(), '2023-12-31T23:59:00Z')).toMatchObject({ state: 'attention', label: expect.stringContaining('outside') });
    expect(policyCover(policy(), '2027-01-01T00:00:00Z').state).toBe('attention');
  });

  it('is amber for an expired policy even when the date is inside its period', () => {
    const cover = policyCover(policy({ status: 'Expired' }), '2025-06-01T12:00:00Z');
    expect(cover.state).toBe('attention');
    expect(cover.label).toContain('expired');
  });
});

describe('helpers', () => {
  it('requires a reason only for withdrawal and reopening', () => {
    expect(transitionNeedsReason('Withdrawn')).toBe(true);
    expect(transitionNeedsReason('Reopened')).toBe(true);
    expect(transitionNeedsReason('Closed')).toBe(false);
    expect(transitionNeedsReason('Open')).toBe(false);
  });

  it('splits camel case for display', () => {
    expect(splitCamelCase('ThirdParty')).toBe('Third Party');
    expect(splitCamelCase('SubrogationRecoverable')).toBe('Subrogation Recoverable');
    expect(splitCamelCase('Indemnity')).toBe('Indemnity');
  });
});
