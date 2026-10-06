import { describe, expect, it } from 'vitest';
import { eventTone, relatedTab } from './audit-tab';
import { formatBytes, validateUpload } from './documents-tab';

describe('audit presentation', () => {
  it.each([
    ['RESERVE_REJECTED', 'danger'], ['GL_POSTING_FAILED', 'danger'], ['RESERVE_APPROVED', 'pending'],
    ['GL_POSTING_SIMULATED', 'closed'], ['STATUS_CHANGED', 'open'], ['CLAIM_CLOSED', 'open'],
    ['SLA_BREACH_DETECTED', 'reopened'], ['VALIDATION_ISSUE_ADDED', 'reopened'],
    ['DOCUMENT_UPLOADED', 'investigation'], ['PARTY_ADDED', 'investigation'], ['SOMETHING_NEW', 'neutral'],
  ])('%s is shown as %s', (type, tone) => {
    expect(eventTone(type)).toBe(tone);
  });

  it('links related entities to the tab where they can be seen', () => {
    expect(relatedTab('ReserveHistory')).toBe('reserves');
    expect(relatedTab('ClaimDocument')).toBe('documents');
    expect(relatedTab('ClaimParty')).toBe('parties');
    expect(relatedTab('Claim')).toBeNull();
    expect(relatedTab(null)).toBeNull();
  });
});

describe('document upload checks', () => {
  const file = (name: string, size = 10) => new File([new Uint8Array(size)], name);

  it('accepts the allowed types regardless of case', () => {
    for (const name of ['a.pdf', 'a.JPG', 'a.jpeg', 'a.png', 'a.docx', 'a.xlsx', 'a.txt', 'a.csv']) {
      expect(validateUpload(file(name))).toBeNull();
    }
  });

  it('rejects other types, empty files and files over 50 MB', () => {
    expect(validateUpload(file('run.exe'))).toMatch(/not an allowed type/);
    expect(validateUpload(file('noextension'))).toMatch(/not an allowed type/);
    expect(validateUpload(file('a.pdf', 0))).toMatch(/empty/);
    expect(validateUpload({ name: 'big.pdf', size: 50 * 1024 * 1024 + 1 } as File)).toMatch(/50 MB/);
    expect(validateUpload({ name: 'edge.pdf', size: 50 * 1024 * 1024 } as File)).toBeNull();
  });

  it('formats sizes', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1536)).toBe('1.5 KB');
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB');
  });
});
