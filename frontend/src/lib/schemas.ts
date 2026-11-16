import { z } from 'zod'
import { phoneAdvice, phoneProblem } from '@/lib/phone'

// These mirror the server's rules so people see problems before submitting; the server still checks everything.

export const email = z.string().trim().min(1, 'Enter your email address.').email('Enter a valid email address.')

export const password = z
  .string()
  .min(10, 'Use at least 10 characters.')
  .max(128, 'Keep it under 128 characters.')
  .regex(/[a-z]/, 'Include a lower-case letter.')
  .regex(/[A-Z]/, 'Include an upper-case letter.')
  .regex(/\d/, 'Include a number.')

export const personName = (what: string, whose = 'your') => z.string().trim().min(1, `Enter ${whose} ${what}.`).max(100, `Keep the ${what} under 100 characters.`)

export const phone = z
  .string()
  .trim()
  .max(30, 'Keep the phone number under 30 characters.')
  .refine((v) => phoneProblem(v) === null, phoneAdvice)
  .optional()

export const authCode = z
  .string()
  .trim()
  .regex(/^\d{6}$/, 'Enter the 6-digit code from your authenticator app.')

export const newPassword = z
  .object({ password, confirm: z.string() })
  .refine((v) => v.password === v.confirm, { message: 'The passwords don’t match.', path: ['confirm'] })
