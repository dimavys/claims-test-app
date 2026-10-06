import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { ApiException, describeError, toApiError } from './api-error';

describe('toApiError', () => {
  it('keeps the server error shape', () => {
    const body = { type: 'ValidationError', title: 'One or more validation errors occurred.', status: 422, errors: { LossDate: ['Loss date cannot be in the future.'] } };
    expect(toApiError(new HttpErrorResponse({ status: 422, error: body }))).toEqual(body);
  });

  it('explains a network failure', () => {
    const error = toApiError(new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') }));
    expect(error.type).toBe('Network');
    expect(error.title).toMatch(/cannot reach the server/i);
  });

  it('falls back for non-JSON server errors such as a proxy page', () => {
    const error = toApiError(new HttpErrorResponse({ status: 502, statusText: 'Bad Gateway', error: '<html>' }));
    expect(error).toMatchObject({ type: 'ServerError', status: 502, title: 'Bad Gateway' });
  });
});

describe('describeError', () => {
  it('shows only the title when there is nothing more specific', () => {
    expect(describeError({ type: 'NotFound', title: 'Claim was not found.', status: 404 })).toBe('Claim was not found.');
  });

  it('appends up to three specific messages and counts the rest', () => {
    const message = describeError({
      type: 'ValidationError', title: 'Invalid.', status: 422,
      errors: { A: ['one', 'two'], B: ['three', 'four'], C: ['one'] },
    });
    expect(message).toBe('Invalid. one · two · three (+1 more)');
  });

  it('includes blocking conditions and never repeats the title', () => {
    expect(describeError({
      type: 'ValidationError', title: 'Claim cannot move.', status: 422,
      errors: { StatusTransition: ['Claim cannot move.'] }, blockingConditions: ['A Claimant is required.'],
    })).toBe('Claim cannot move. A Claimant is required.');
  });
});

describe('ApiException', () => {
  it('flattens every message once', () => {
    const e = new ApiException({
      type: 'ValidationError', title: 't', status: 422,
      errors: { A: ['x', 'y'], B: ['x'] }, blockingConditions: ['y', 'z'],
    });
    expect(e.allMessages).toEqual(['x', 'y', 'z']);
    expect(e.status).toBe(422);
    expect(e.message).toBe('t');
  });
});
