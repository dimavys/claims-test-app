import { TestBed } from '@angular/core/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { describe, expect, it, vi } from 'vitest';
import { ClaimStatus } from '../core/models/api.models';
import { ApprovalBadge, PostingBadge, StatusBadge, statusTone } from './badge';
import { ConfirmDialog } from './confirm-dialog';

describe('status badge', () => {
  it.each<[ClaimStatus, string, string]>([
    ['Draft', 'draft', 'Draft'], ['Open', 'open', 'Open'], ['UnderInvestigation', 'investigation', 'Under investigation'],
    ['PendingPayment', 'pending', 'Pending payment'], ['Closed', 'closed', 'Closed'],
    ['Reopened', 'reopened', 'Reopened'], ['Withdrawn', 'withdrawn', 'Withdrawn'],
  ])('%s is shown with the %s colour and label "%s"', (status, tone, label) => {
    const fixture = TestBed.createComponent(StatusBadge);
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(statusTone(status)).toBe(tone);
    expect(el.querySelector('.badge')?.classList.contains(`tone-${tone}`)).toBe(true);
    expect(el.textContent?.trim()).toBe(label);
  });
});

describe('reserve badges', () => {
  it('labels approval statuses readably and colours rejection red', () => {
    const fixture = TestBed.createComponent(ApprovalBadge);
    fixture.componentRef.setInput('status', 'PendingApproval');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.trim()).toBe('Pending Approval');
    expect(fixture.nativeElement.querySelector('.tone-reopened')).not.toBeNull();

    fixture.componentRef.setInput('status', 'Rejected');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.tone-danger')).not.toBeNull();
  });

  it('shows GL posting state', () => {
    const fixture = TestBed.createComponent(PostingBadge);
    fixture.componentRef.setInput('status', 'Failed');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.trim()).toBe('GL Failed');
    expect(fixture.nativeElement.querySelector('.tone-danger')).not.toBeNull();
  });
});

describe('ConfirmDialog', () => {
  it('shows the title, message and bullet items', () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { title: 'Create claim with warnings?', message: 'Please note:', items: ['No risk objects are linked.'], confirmLabel: 'Create' } },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(ConfirmDialog);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Create claim with warnings?');
    expect(text).toContain('No risk objects are linked.');
    expect(fixture.nativeElement.querySelector('[data-testid="confirm-ok"]').textContent.trim()).toBe('Create');
  });
});
