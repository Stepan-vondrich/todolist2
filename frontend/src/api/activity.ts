// Activity-date filter client: asks the backend which todos had activity of the
// given kinds within [from, to]. Returns the matching todo ids.
export async function fetchActivity(
  from: string,
  to: string,
  types: string[],
): Promise<number[]> {
  const params = new URLSearchParams()
  if (from) params.set('from', from)
  if (to) params.set('to', to)
  if (types.length > 0) params.set('types', types.join(','))

  const res = await fetch(`/api/activity?${params.toString()}`)
  if (!res.ok) throw new Error('Failed to fetch activity')
  return res.json()
}
