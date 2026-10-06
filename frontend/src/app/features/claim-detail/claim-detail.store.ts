import { Injectable, computed, inject, signal } from '@angular/core';
import { ClaimsApi } from '../../core/api/claims-api';
import { silent } from '../../core/api/api-context';
import { Auth } from '../../core/auth/auth';
import { ApiException } from '../../core/http/api-error';
import { ClaimDetail, NextStatus } from '../../core/models/api.models';
import { meetsMinimumRole } from '../../core/util/domain';

/**
 * State for one open claim, shared by the header and every tab. Provided by the page component, so each visit gets a
 * fresh instance and nothing leaks between claims.
 */
@Injectable()
export class ClaimDetailStore {
  private readonly api = inject(ClaimsApi);
  private readonly auth = inject(Auth);

  readonly claim = signal<ClaimDetail | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly failed = signal(false);

  private id = '';

  readonly activeClaimants = computed(() =>
    this.claim()?.parties.filter(p => p.isActive && p.partyRole === 'Claimant') ?? []);

  /** Transitions the signed-in role may perform (the API enforces this too; this keeps the menu honest). */
  readonly availableTransitions = computed<NextStatus[]>(() =>
    (this.claim()?.validNextStatuses ?? []).filter(t => meetsMinimumRole(this.auth.role(), t.minimumRole)));

  /** Reserves can only change while the claim is live and linked to a policy (BR-C-06). */
  readonly canChangeReserves = computed(() => {
    const c = this.claim();
    return !!c && !!c.policyId && c.status !== 'Closed' && c.status !== 'Withdrawn';
  });

  load(id: string): void {
    this.id = id;
    this.loading.set(true);
    this.notFound.set(false);
    this.failed.set(false);
    this.claim.set(null);
    this.fetch(false);
  }

  /** Re-reads the claim after a change without blanking the screen. */
  reload(): void {
    if (this.id) this.fetch(true);
  }

  private fetch(quiet: boolean): void {
    this.api.get(this.id).subscribe({
      next: claim => { this.claim.set(claim); this.loading.set(false); },
      error: (e: unknown) => {
        this.loading.set(false);
        if (quiet) return;
        if (e instanceof ApiException && e.status === 404) this.notFound.set(true);
        else this.failed.set(true);
      },
    });
  }

  /** Background refresh used by polling; skips the progress bar and error toast. */
  refreshSilently(): void {
    if (!this.id) return;
    this.api.get(this.id, silent()).subscribe({ next: claim => this.claim.set(claim), error: () => undefined });
  }
}
