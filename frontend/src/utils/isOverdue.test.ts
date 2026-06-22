import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { isOverdue } from './isOverdue';

describe('isOverdue', () => {
  beforeEach(() => {
    // Fix "today" to 2026-04-18T12:00:00.000Z for deterministic tests
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-04-18T12:00:00.000Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns false when dueDate is null', () => {
    expect(isOverdue(null)).toBe(false);
  });

  it('returns true when due date is in the past (yesterday)', () => {
    expect(isOverdue('2026-04-17')).toBe(true);
  });

  it('returns true when due date is well in the past', () => {
    expect(isOverdue('2025-01-01')).toBe(true);
  });

  it('returns false when due date is today', () => {
    expect(isOverdue('2026-04-18')).toBe(false);
  });

  it('returns false when due date is in the future (tomorrow)', () => {
    expect(isOverdue('2026-04-19')).toBe(false);
  });

  it('returns false when due date is well in the future', () => {
    expect(isOverdue('2027-12-31')).toBe(false);
  });
});
