import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { ClaimsApi } from '../../../core/api/claims-api';
import { ApiException } from '../../../core/http/api-error';
import { Notifier } from '../../../core/http/notifier';
import { ClaimDocument } from '../../../core/models/api.models';
import { Badge } from '../../../shared/badge';
import { EmptyState } from '../../../shared/empty-state';
import { Skeleton } from '../../../shared/skeleton';
import { ClaimDetailStore } from '../claim-detail.store';

const MAX_BYTES = 50 * 1024 * 1024;
const ALLOWED_EXTENSIONS = ['pdf', 'jpg', 'jpeg', 'png', 'docx', 'xlsx', 'txt', 'csv'];
const DOCUMENT_TYPES = ['PoliceReport', 'MedicalReport', 'Invoice', 'Photo', 'Other'];

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** Returns an error message for a file the server would refuse, or null when it looks acceptable. */
export function validateUpload(file: File): string | null {
  const ext = file.name.split('.').pop()?.toLowerCase() ?? '';
  if (!ALLOWED_EXTENSIONS.includes(ext)) return `“${file.name}” is not an allowed type. Allowed: PDF, JPEG, PNG, DOCX, XLSX, TXT, CSV.`;
  if (file.size === 0) return `“${file.name}” is empty.`;
  if (file.size > MAX_BYTES) return `“${file.name}” is ${formatBytes(file.size)}; the limit is 50 MB.`;
  return null;
}

@Component({
  selector: 'app-documents-tab',
  imports: [DatePipe, FormsModule, MatTableModule, MatButtonModule, MatIconModule, MatSelectModule, MatFormFieldModule, MatProgressBarModule, Badge, EmptyState, Skeleton],
  template: `
    <div class="wrap">
      <div class="head">
        <h2>Documents</h2>
        <div class="upload">
          <mat-form-field class="type">
            <mat-label>Document type</mat-label>
            <mat-select [(ngModel)]="documentType" data-testid="document-type">
              @for (t of types; track t) { <mat-option [value]="t">{{ t }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <input #picker type="file" hidden (change)="onPick($event)" accept=".pdf,.jpg,.jpeg,.png,.docx,.xlsx,.txt,.csv" data-testid="file-input" />
          <button mat-flat-button (click)="picker.click()" [disabled]="uploading()" data-testid="upload-button"><mat-icon>upload_file</mat-icon> Upload</button>
        </div>
      </div>

      @if (uploading()) {
        <div class="progress" data-testid="upload-progress">
          <span>Uploading {{ fileName() }}… {{ percent() }}%</span>
          <mat-progress-bar mode="determinate" [value]="percent()" />
        </div>
      }
      @if (error()) { <div class="error" role="alert"><mat-icon>error</mat-icon><span>{{ error() }}</span></div> }

      @if (loading()) {
        <app-skeleton [lines]="4" />
      } @else if (documents().length === 0) {
        <app-empty-state icon="folder_off" title="No documents yet" message="Upload police reports, invoices, photos or medical reports (PDF, JPEG, PNG, DOCX, XLSX, TXT, CSV; up to 50 MB)." />
      } @else {
        <table mat-table [dataSource]="documents()" data-testid="documents-table">
          <ng-container matColumnDef="name">
            <th mat-header-cell *matHeaderCellDef>Name</th>
            <td mat-cell *matCellDef="let d"><mat-icon class="file-icon">description</mat-icon> {{ d.documentName }}</td>
          </ng-container>
          <ng-container matColumnDef="type">
            <th mat-header-cell *matHeaderCellDef>Type</th>
            <td mat-cell *matCellDef="let d"><app-badge tone="neutral">{{ d.documentType }}</app-badge></td>
          </ng-container>
          <ng-container matColumnDef="uploaded">
            <th mat-header-cell *matHeaderCellDef>Uploaded</th>
            <td mat-cell *matCellDef="let d">{{ d.uploadedAt | date: 'medium' }}</td>
          </ng-container>
          <ng-container matColumnDef="by">
            <th mat-header-cell *matHeaderCellDef>Uploaded by</th>
            <td mat-cell *matCellDef="let d">{{ d.uploadedByName ?? '—' }}</td>
          </ng-container>
          <ng-container matColumnDef="size">
            <th mat-header-cell *matHeaderCellDef class="num">Size</th>
            <td mat-cell *matCellDef="let d" class="num">{{ size(d.fileSizeBytes) }}</td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let d" class="end">
              <button mat-stroked-button (click)="download(d)" [disabled]="!d.downloadUrl" data-testid="download-document"><mat-icon>download</mat-icon> Download</button>
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
      }
    </div>
  `,
  styles: `
    .wrap { padding: 20px; }
    .head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
    h2 { margin: 0; font: var(--mat-sys-title-medium); }
    .upload { display: flex; align-items: center; gap: 12px; }
    .type { width: 200px; }
    .progress { margin: 8px 0 16px; display: flex; flex-direction: column; gap: 6px; }
    .error { display: flex; gap: 8px; align-items: center; background: var(--status-danger-bg); color: var(--status-danger-fg); padding: 8px 12px; border-radius: 8px; margin: 8px 0; }
    table { width: 100%; }
    th.num, td.num { text-align: right; font-variant-numeric: tabular-nums; }
    td.end { text-align: right; }
    .file-icon { vertical-align: middle; margin-right: 4px; color: var(--mat-sys-primary); }
  `,
})
export class DocumentsTab {
  private readonly store = inject(ClaimDetailStore);
  private readonly api = inject(ClaimsApi);
  private readonly notifier = inject(Notifier);

  protected readonly types = DOCUMENT_TYPES;
  protected readonly columns = ['name', 'type', 'uploaded', 'by', 'size', 'actions'];
  protected readonly size = formatBytes;
  protected documentType = 'Other';

  protected readonly documents = signal<ClaimDocument[]>([]);
  protected readonly loading = signal(true);
  protected readonly uploading = signal(false);
  protected readonly percent = signal(0);
  protected readonly fileName = signal('');
  protected readonly error = signal<string | null>(null);

  private readonly claimId = computed(() => this.store.claim()?.id);

  constructor() {
    effect(() => {
      if (this.claimId()) untracked(() => this.load());
    });
  }

  protected download(doc: ClaimDocument): void {
    // The link is a short-lived storage URL: the bytes come straight from storage, not through the API.
    if (doc.downloadUrl) window.open(doc.downloadUrl, '_blank', 'noopener');
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow picking the same file again
    if (!file) return;

    const problem = validateUpload(file);
    if (problem) { this.error.set(problem); return; }

    const claimId = this.claimId();
    if (!claimId) return;

    this.error.set(null);
    this.uploading.set(true);
    this.percent.set(0);
    this.fileName.set(file.name);

    this.api.upload(claimId, file, this.documentType).subscribe({
      next: progress => {
        this.percent.set(progress.percent);
        if (progress.document) {
          this.uploading.set(false);
          this.notifier.success(`${progress.document.documentName} uploaded.`);
          this.load();
          this.store.reload();
        }
      },
      error: (e: unknown) => {
        this.uploading.set(false);
        if (e instanceof ApiException && e.allMessages.length) this.error.set(e.allMessages.join(' '));
      },
    });
  }

  private load(): void {
    const claimId = this.claimId();
    if (!claimId) return;
    this.api.documents(claimId).subscribe({
      next: docs => { this.documents.set(docs); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
}
