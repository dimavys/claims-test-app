import { describe, expect, it } from 'vitest';
import { Policy } from '../../core/models/api.models';
import {
  FnolDraft, buildCreateClaimRequest, groupServerErrorsByStep, stepForServerField, toLocalInputValue,
} from './fnol-payload';

const policy: Policy = {
  id: 'p-1', policyNumber: 'POL-2024-001001', clientName: 'Meridian', effectiveDate: '2024-01-01',
  expirationDate: '2026-12-31', status: 'Active', coverageTypes: ['Vehicle'],
};

const draft = (over: Partial<FnolDraft> = {}): FnolDraft => ({
  policy, lossDateLocal: '2026-03-04T09:30', causeOfLossCode: 'COL-FIRE', lossDescription: '  Fire in the warehouse overnight  ',
  lossLocation: '  Dock 4 ', estimatedLossAmount: 1200,
  parties: [{ partyRole: 'Claimant', partyType: 'Person', firstName: 'Ada', lastName: 'Lovelace' }],
  riskObjects: [{ assetType: 'Property', assetDescription: 'Warehouse' }], reserve: null, ...over,
});

describe('buildCreateClaimRequest', () => {
  it('maps the draft to the API contract and trims text', () => {
    const request = buildCreateClaimRequest(draft());
    expect(request).toMatchObject({
      policyId: 'p-1', causeOfLossCode: 'COL-FIRE', lossDescription: 'Fire in the warehouse overnight',
      lossLocation: 'Dock 4', estimatedLossAmount: 1200, initialReserve: null,
    });
    expect(request.parties).toHaveLength(1);
    expect(request.riskObjects).toHaveLength(1);
  });

  it('sends the loss date as an ISO instant of the local time entered', () => {
    const request = buildCreateClaimRequest(draft());
    expect(new Date(request.lossDate).getTime()).toBe(new Date('2026-03-04T09:30').getTime());
    expect(request.lossDate).toMatch(/Z$/);
  });

  it('uses a null policy for an unknown policy and drops a blank location', () => {
    const request = buildCreateClaimRequest(draft({ policy: null, lossLocation: '   ' }));
    expect(request.policyId).toBeNull();
    expect(request.lossLocation).toBeNull();
  });

  it('includes the initial reserve with a trimmed reason', () => {
    const request = buildCreateClaimRequest(draft({ reserve: { component: 'Indemnity', amount: 50_000, reason: ' Big loss ' } }));
    expect(request.initialReserve).toEqual({ component: 'Indemnity', amount: 50_000, changeReason: 'Big loss' });
  });

  it('leaves the reserve reason null when blank so the server default applies', () => {
    const request = buildCreateClaimRequest(draft({ reserve: { component: 'Expense', amount: 10, reason: '' } }));
    expect(request.initialReserve?.changeReason).toBeNull();
  });
});

describe('server error placement', () => {
  it.each([
    ['LossDate', 0], ['LossDescription', 0], ['CauseOfLossCode', 0], ['PolicyId', 0],
    ['Parties[0].FirstName', 1], ['ClaimParties', 1], ['RiskObjects[1].AssetDescription', 1],
    ['InitialReserve.Amount', 2], ['ReserveAmount', 2], ['ReserveComponent', 2], ['Something.Else', 0],
  ])('%s belongs to step %s', (field, step) => {
    expect(stepForServerField(field)).toBe(step);
  });

  it('groups and de-duplicates messages per step', () => {
    const grouped = groupServerErrorsByStep({
      LossDate: ['Loss date cannot be in the future.'],
      CauseOfLossCode: ['Cause of loss code is not recognised or is inactive.', 'Loss date cannot be in the future.'],
      'Parties[0].LastName': ['Last name is required.'],
      ReserveAmount: ['Reserve amount must be greater than zero.'],
    });
    expect(grouped[0]).toEqual(['Loss date cannot be in the future.', 'Cause of loss code is not recognised or is inactive.']);
    expect(grouped[1]).toEqual(['Last name is required.']);
    expect(grouped[2]).toEqual(['Reserve amount must be greater than zero.']);
  });
});

describe('toLocalInputValue', () => {
  it('formats local time for a datetime-local input', () => {
    expect(toLocalInputValue(new Date(2026, 0, 5, 7, 4))).toBe('2026-01-05T07:04');
  });
});
