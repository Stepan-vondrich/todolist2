/**
 * Returns true if the given due date string is strictly in the past (before today).
 * Returns false if dueDate is null or today or in the future.
 *
 * @param dueDate - ISO date string (e.g. "2026-04-17") or null
 */
export function isOverdue(dueDate: string | null): boolean {
  if (dueDate === null) {
    return false;
  }

  const today = new Date();
  // Zero out time so we compare dates only (not datetimes)
  today.setHours(0, 0, 0, 0);

  const due = new Date(dueDate.split('T')[0] + 'T00:00:00');

  return due < today;
}
