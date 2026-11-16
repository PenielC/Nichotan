import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from './api'

/** Puts the server's per-field messages on the form's fields; returns the message for the form as a whole. */
export function applyServerErrors<T extends FieldValues>(error: unknown, fields: Path<T>[], setError: UseFormSetError<T>): string {
  if (!(error instanceof ApiError)) return 'We couldn’t reach Nichotan. Check your connection and try again.'
  let placed = false
  for (const field of fields) {
    const message = error.fieldMessage(field)
    if (message) {
      setError(field, { message })
      placed = true
    }
  }
  return placed ? 'Please check the highlighted fields.' : error.message
}

export const passwordRule = 'At least 10 characters, with upper and lower case letters and a number.'
