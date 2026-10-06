import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, ViewChild, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, FormControl, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatStepper, MatStepperModule } from '@angular/material/stepper';
import { Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, filter, of, startWith, switchMap } from 'rxjs';
import { ClaimsApi } from '../../core/api/claims-api';
import { ReferenceApi } from '../../core/api/reference-api';
import { ApiException } from '../../core/http/api-error';
import { Notifier } from '../../core/http/notifier';
import {
  AssetType, CauseOfLossCode, PartyInput, PartyRole, PartyType, Policy, ReserveComponentType, RiskObjectInput,
  ValidationReport,
} from '../../core/models/api.models';
import {
  authorityMessage, isValidReserveAmount, policyCover, requiredAuthority, splitCamelCase,
} from '../../core/util/domain';
import { Badge } from '../../shared/badge';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import {
  FnolDraft, SERVER_FIELD_TO_CONTROL, buildCreateClaimRequest, groupServerErrorsByStep, toLocalInputValue,
} from './fnol-payload';

const PARTY_ROLES: PartyRole[] = ['Claimant', 'Insured', 'ThirdParty', 'Witness', 'Attorney'];
const ASSET_TYPES: AssetType[] = ['Vehicle', 'Property', 'Person', 'Equipment', 'Other'];
const RESERVE_COMPONENTS: ReserveComponentType[] = ['Indemnity', 'Expense', 'ALAE', 'SubrogationRecoverable'];

const notInFuture = (c: AbstractControl): ValidationErrors | null =>
  c.value && new Date(c.value).getTime() > Date.now() ? { future: true } : null;

const minTrimmedLength = (min: number) => (c: AbstractControl): ValidationErrors | null =>
  String(c.value ?? '').trim().length >= min ? null : { minTrimmed: { required: min } };

/** The control must hold a chosen option (an object), not free text. */
const mustPick = (c: AbstractControl): ValidationErrors | null =>
  c.value && typeof c.value === 'object' ? null : { pick: true };

@Component({
  selector: 'app-fnol-intake',
  imports: [
    ReactiveFormsModule, RouterLink, CurrencyPipe, DatePipe,
    MatStepperModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatIconModule,
    MatAutocompleteModule, MatSlideToggleModule, MatProgressSpinnerModule, Badge,
  ],
  templateUrl: './fnol-intake.html',
  styleUrl: './fnol-intake.scss',
})
export class FnolIntake {
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly claimsApi = inject(ClaimsApi);
  private readonly referenceApi = inject(ReferenceApi);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  @ViewChild(MatStepper) private stepper?: MatStepper;

  protected readonly partyRoles = PARTY_ROLES;
  protected readonly assetTypes = ASSET_TYPES;
  protected readonly reserveComponents = RESERVE_COMPONENTS;
  protected readonly maxLossDate = toLocalInputValue(new Date());
  protected readonly splitCamelCase = splitCamelCase;

  /** One key per wizard: if the response to Create is lost and the user retries, the API replays instead of duplicating. */
  private readonly idempotencyKey = crypto.randomUUID();

  // ------------------------------------------------------------------ step 1: policy & loss details

  protected readonly step1 = this.fb.group({
    unknownPolicy: [false],
    policy: new FormControl<string | Policy | null>('', {
      validators: [(c: AbstractControl): ValidationErrors | null => {
        const unknown = c.parent?.get('unknownPolicy')?.value;
        return unknown || (c.value && typeof c.value === 'object') ? null : { policyRequired: true };
      }],
    }),
    lossDate: ['', [Validators.required, notInFuture]],
    cause: new FormControl<string | CauseOfLossCode | null>('', { validators: [mustPick] }),
    lossDescription: ['', [minTrimmedLength(20)]],
    lossLocation: ['', Validators.maxLength(500)],
    estimatedLossAmount: new FormControl<number | null>(null, { validators: [Validators.min(0)] }),
  });

  private readonly policyValue = toSignal(this.step1.controls.policy.valueChanges, { initialValue: '' as string | Policy | null });
  protected readonly unknownPolicy = toSignal(this.step1.controls.unknownPolicy.valueChanges, { initialValue: false });
  private readonly lossDateValue = toSignal(this.step1.controls.lossDate.valueChanges, { initialValue: '' });
  private readonly causeValue = toSignal(this.step1.controls.cause.valueChanges, { initialValue: '' as string | CauseOfLossCode | null });
  protected readonly descriptionText = toSignal(
    this.step1.controls.lossDescription.valueChanges.pipe(startWith(''), distinctUntilChanged()), { initialValue: '' });

  protected readonly selectedPolicy = computed(() => {
    const v = this.policyValue();
    return v && typeof v === 'object' ? v : null;
  });
  private readonly lossDateIso = computed(() => {
    const v = this.lossDateValue();
    return v && !Number.isNaN(new Date(v).getTime()) ? new Date(v).toISOString() : null;
  });
  protected readonly cover = computed(() => policyCover(this.selectedPolicy(), this.lossDateIso()));
  protected readonly trimmedDescriptionLength = computed(() => String(this.descriptionText() ?? '').trim().length);

  protected readonly policyOptions = toSignal(
    this.step1.controls.policy.valueChanges.pipe(
      startWith(''),
      filter((v): v is string => typeof v === 'string'),
      debounceTime(250),
      distinctUntilChanged(),
      switchMap(q => this.referenceApi.searchPolicies(q.trim()).pipe(catchError(() => of([] as Policy[])))),
    ), { initialValue: [] as Policy[] });

  private readonly causes = toSignal(this.referenceApi.causeOfLossCodes(), { initialValue: [] as CauseOfLossCode[] });
  protected readonly causeOptions = computed(() => {
    const v = this.causeValue();
    const text = (typeof v === 'string' ? v : v?.name ?? '').trim().toLowerCase();
    return this.causes().filter(c => !text || c.name.toLowerCase().includes(text) || c.code.toLowerCase().includes(text));
  });
  private readonly chosenCause = computed(() => {
    const v = this.causeValue();
    return v && typeof v === 'object' ? v : null;
  });

  protected readonly displayPolicy = (p: Policy | string | null): string =>
    p && typeof p === 'object' ? `${p.policyNumber} — ${p.clientName}` : (p ?? '');
  protected readonly displayCause = (c: CauseOfLossCode | string | null): string =>
    c && typeof c === 'object' ? c.name : (c ?? '');

  // ------------------------------------------------------------------ step 2: parties & risk objects

  protected readonly parties = signal<PartyInput[]>([]);
  protected readonly riskObjects = signal<RiskObjectInput[]>([]);
  protected readonly addingParty = signal(false);
  protected readonly addingRisk = signal(false);

  protected readonly hasClaimant = computed(() => this.parties().some(p => p.partyRole === 'Claimant'));

  protected readonly partyForm = this.fb.group({
    partyRole: ['Claimant' as PartyRole],
    partyType: ['Person' as PartyType],
    firstName: [''],
    lastName: [''],
    companyName: [''],
    email: ['', Validators.email],
    phone: [''],
  });
  protected readonly partyType = toSignal(this.partyForm.controls.partyType.valueChanges, { initialValue: 'Person' as PartyType });

  protected readonly riskForm = this.fb.group({
    assetType: ['Vehicle' as AssetType],
    assetDescription: ['', [Validators.required, Validators.maxLength(500)]],
    damageDescription: [''],
    assetReference: ['', Validators.maxLength(255)],
  });

  // ------------------------------------------------------------------ step 3: reserve & review

  protected readonly includeReserve = signal(false);
  protected readonly reserveForm = this.fb.group({
    component: ['Indemnity' as ReserveComponentType],
    amount: new FormControl<number | null>(null),
    reason: [''],
  });
  private readonly reserveAmount = toSignal(this.reserveForm.controls.amount.valueChanges, { initialValue: null as number | null });
  private readonly reserveComponent = toSignal(this.reserveForm.controls.component.valueChanges, { initialValue: 'Indemnity' as ReserveComponentType });

  protected readonly reserveValid = computed(() =>
    !this.includeReserve() || isValidReserveAmount(this.reserveComponent(), this.reserveAmount()));
  protected readonly authority = computed(() => {
    const amount = this.reserveAmount();
    if (!this.includeReserve() || amount === null || !isValidReserveAmount(this.reserveComponent(), amount)) return null;
    const level = requiredAuthority(amount);
    return { level, message: authorityMessage(level) };
  });

  protected readonly report = signal<ValidationReport | null>(null);
  protected readonly validating = signal(false);

  // ------------------------------------------------------------------ submission

  protected readonly submitting = signal(false);
  protected readonly serverErrors = signal<[string[], string[], string[]]>([[], [], []]);
  protected readonly step1Valid = signal(false);
  protected readonly canCreate = computed(() =>
    this.step1Valid() && this.hasClaimant() && this.reserveValid() && !this.submitting());

  constructor() {
    this.step1.statusChanges.pipe(startWith(this.step1.status), takeUntilDestroyed()).subscribe(() => this.step1Valid.set(this.step1.valid));

    // Re-check the policy requirement when the "unknown policy" toggle flips, and drop the stale selection.
    this.step1.controls.unknownPolicy.valueChanges.pipe(takeUntilDestroyed()).subscribe(unknown => {
      if (unknown) {
        this.step1.controls.policy.setValue('');
        this.includeReserve.set(false); // reserves need a policy (BR-C-06)
      }
      this.step1.controls.policy.updateValueAndValidity();
    });
  }

  // ------------------------------------------------------------------ actions

  protected clearPolicy(): void {
    this.step1.controls.policy.setValue('');
  }

  protected toggleAddParty(open: boolean): void {
    this.addingParty.set(open);
    if (open) this.partyForm.reset({ partyRole: this.hasClaimant() ? 'Witness' : 'Claimant', partyType: 'Person' });
  }

  protected saveParty(): void {
    const v = this.partyForm.getRawValue();
    const person = v.partyType === 'Person';
    const missing = person ? !v.firstName.trim() || !v.lastName.trim() : !v.companyName.trim();
    if (missing || this.partyForm.controls.email.invalid) {
      this.partyForm.markAllAsTouched();
      this.partyForm.controls.firstName.setErrors(person && !v.firstName.trim() ? { required: true } : null);
      this.partyForm.controls.lastName.setErrors(person && !v.lastName.trim() ? { required: true } : null);
      this.partyForm.controls.companyName.setErrors(!person && !v.companyName.trim() ? { required: true } : null);
      return;
    }

    this.parties.update(list => [...list, {
      partyRole: v.partyRole, partyType: v.partyType,
      firstName: person ? v.firstName.trim() : null, lastName: person ? v.lastName.trim() : null,
      companyName: person ? null : v.companyName.trim(),
      email: v.email.trim() || null, phone: v.phone.trim() || null,
    }]);
    this.addingParty.set(false);
    this.report.set(null);
  }

  protected removeParty(index: number): void {
    this.parties.update(list => list.filter((_, i) => i !== index));
    this.report.set(null);
  }

  protected partyName(p: PartyInput): string {
    return p.partyType === 'Company' ? (p.companyName ?? '') : `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim();
  }

  protected toggleAddRisk(open: boolean): void {
    this.addingRisk.set(open);
    if (open) this.riskForm.reset({ assetType: 'Vehicle' });
  }

  protected saveRisk(): void {
    if (this.riskForm.invalid) { this.riskForm.markAllAsTouched(); return; }
    const v = this.riskForm.getRawValue();
    this.riskObjects.update(list => [...list, {
      assetType: v.assetType, assetDescription: v.assetDescription.trim(),
      damageDescription: v.damageDescription.trim() || null, assetReference: v.assetReference.trim() || null,
    }]);
    this.addingRisk.set(false);
    this.report.set(null);
  }

  protected removeRisk(index: number): void {
    this.riskObjects.update(list => list.filter((_, i) => i !== index));
    this.report.set(null);
  }

  protected onStepChange(index: number): void {
    // Validation is authoritative on the server: ask it to dry-run the claim when the user reaches the review step.
    if (index === 2) this.refreshReport();
  }

  protected refreshReport(): void {
    if (!this.step1Valid()) return;
    this.validating.set(true);
    this.claimsApi.validate(this.buildRequest()).subscribe({
      next: report => { this.report.set(report); this.validating.set(false); },
      error: () => this.validating.set(false),
    });
  }

  protected create(): void {
    if (!this.canCreate()) return;

    const warnings = this.report()?.warnings ?? [];
    if (warnings.length === 0) {
      this.submit();
      return;
    }

    this.dialog.open(ConfirmDialog, {
      data: {
        title: 'Create claim with warnings?',
        message: 'The claim will be created, but please note:',
        items: warnings,
        confirmLabel: 'Create claim',
      },
    }).afterClosed().subscribe(ok => ok && this.submit());
  }

  private submit(): void {
    this.submitting.set(true);
    this.serverErrors.set([[], [], []]);

    this.claimsApi.create(this.buildRequest(), this.idempotencyKey).subscribe({
      next: created => {
        this.notifier.success(`Claim ${created.claimNumber} created.`);
        void this.router.navigate(['/claims', created.id], { queryParams: { created: created.claimNumber } });
      },
      error: (e: unknown) => {
        this.submitting.set(false);
        if (!(e instanceof ApiException)) return;

        const errors = e.fieldErrors;
        const byStep = groupServerErrorsByStep(errors);
        if (byStep.every(list => list.length === 0) && e.allMessages.length === 0) byStep[2] = [e.message];
        this.serverErrors.set(byStep);
        this.showFieldErrors(errors);

        const first = byStep.findIndex(list => list.length > 0);
        if (first >= 0 && this.stepper) this.stepper.selectedIndex = first;
      },
    });
  }

  /** Puts each server message on its field so the Material error pattern shows it next to the input. */
  private showFieldErrors(errors: Record<string, string[]>): void {
    for (const [field, messages] of Object.entries(errors)) {
      const control = this.step1.get(SERVER_FIELD_TO_CONTROL[field] ?? '');
      if (control) {
        control.setErrors({ ...control.errors, server: messages[0] });
        control.markAsTouched();
      }
    }
  }

  private buildRequest() {
    const f = this.step1.getRawValue();
    const draft: FnolDraft = {
      policy: this.selectedPolicy(),
      lossDateLocal: f.lossDate,
      causeOfLossCode: this.chosenCause()?.code ?? '',
      lossDescription: f.lossDescription,
      lossLocation: f.lossLocation,
      estimatedLossAmount: f.estimatedLossAmount,
      parties: this.parties(),
      riskObjects: this.riskObjects(),
      reserve: this.includeReserve() && this.reserveAmount() !== null
        ? { component: this.reserveComponent(), amount: this.reserveAmount()!, reason: this.reserveForm.controls.reason.value }
        : null,
    };
    return buildCreateClaimRequest(draft);
  }

  /** Values for the read-only review table (read on demand so it never shows stale form state). */
  protected summary() {
    const f = this.step1.getRawValue();
    return {
      policy: this.selectedPolicy(),
      lossDate: this.lossDateIso(),
      cause: this.chosenCause(),
      description: String(f.lossDescription ?? '').trim(),
      location: f.lossLocation,
      estimated: f.estimatedLossAmount,
    };
  }
}
