import {
  CreateClaimRequest, PartyInput, Policy, ReserveComponentType, RiskObjectInput,
} from '../../core/models/api.models';

export interface FnolDraft {
  policy: Policy | null;
  /** Value of an <input type="datetime-local"> (local time, no offset). */
  lossDateLocal: string;
  causeOfLossCode: string;
  lossDescription: string;
  lossLocation: string;
  estimatedLossAmount: number | null;
  parties: PartyInput[];
  riskObjects: RiskObjectInput[];
  reserve: { component: ReserveComponentType; amount: number; reason: string } | null;
}

/** The intake form is a UX concern only: everything is submitted in one call (FRS §5.2). */
export function buildCreateClaimRequest(draft: FnolDraft): CreateClaimRequest {
  const text = (s: string) => (s.trim() ? s.trim() : null);

  return {
    policyId: draft.policy?.id ?? null,
    lossDate: new Date(draft.lossDateLocal).toISOString(),
    lossDescription: draft.lossDescription.trim(),
    causeOfLossCode: draft.causeOfLossCode,
    lossLocation: text(draft.lossLocation),
    estimatedLossAmount: draft.estimatedLossAmount,
    parties: draft.parties,
    riskObjects: draft.riskObjects,
    initialReserve: draft.reserve
      ? { component: draft.reserve.component, amount: draft.reserve.amount, changeReason: text(draft.reserve.reason) }
      : null,
  };
}

/** Which wizard step owns a server-side validation key (so errors can be shown where they can be fixed). */
export function stepForServerField(field: string): 0 | 1 | 2 {
  const key = field.split(/[.[]/)[0];
  switch (key) {
    case 'Parties':
    case 'ClaimParties':
    case 'RiskObjects':
      return 1;
    case 'InitialReserve':
    case 'ReserveAmount':
    case 'ReserveComponent':
      return 2;
    default:
      return 0; // LossDate, LossDescription, CauseOfLossCode, PolicyId, LossLocation, EstimatedLossAmount, …
  }
}

export function groupServerErrorsByStep(errors: Record<string, string[]>): [string[], string[], string[]] {
  const steps: [string[], string[], string[]] = [[], [], []];
  for (const [field, messages] of Object.entries(errors)) {
    steps[stepForServerField(field)].push(...messages);
  }
  return steps.map(list => [...new Set(list)]) as [string[], string[], string[]];
}

/** Form control that shows each server error key, e.g. `LossDate` → `lossDate`. */
export const SERVER_FIELD_TO_CONTROL: Record<string, string> = {
  LossDate: 'lossDate',
  LossDescription: 'lossDescription',
  CauseOfLossCode: 'cause',
  PolicyId: 'policy',
  LossLocation: 'lossLocation',
  EstimatedLossAmount: 'estimatedLossAmount',
};

/** `yyyy-MM-ddTHH:mm` in local time, the format <input type="datetime-local"> expects. */
export function toLocalInputValue(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
