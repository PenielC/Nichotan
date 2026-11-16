// Dates are stored in UTC and shown in the viewer's own time zone and locale.

export function formatDate(utc: string): string {
  return new Date(utc).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

export function formatDateTime(utc: string): string {
  return new Date(utc).toLocaleString(undefined, { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })
}
