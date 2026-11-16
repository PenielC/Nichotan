import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link, useNavigate } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Field, FormError, PasswordField } from '@/components/form'
import { applyServerErrors, passwordRule } from '@/lib/form-errors'
import { AuthLayout } from '@/layouts/AuthLayout'
import { post, type Session } from '@/lib/api'
import { useAuth } from '@/lib/auth'
import { PhoneHint } from '@/components/phone-hint'
import { email, password, personName, phone } from '@/lib/schemas'

const schema = z
  .object({ firstName: personName('first name'), lastName: personName('last name'), email, phone, password, confirm: z.string() })
  .refine((v) => v.password === v.confirm, { message: 'The passwords don’t match.', path: ['confirm'] })
type Values = z.infer<typeof schema>

export default function Register() {
  const { begin } = useAuth()
  const navigate = useNavigate()
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { firstName: '', lastName: '', email: '', phone: '', password: '', confirm: '' } })
  const phoneValue = useWatch({ control: form.control, name: 'phone' })
  const { errors, isSubmitting } = form.formState

  const submit = form.handleSubmit(async ({ firstName, lastName, email, phone, password }) => {
    setFormError(null)
    try {
      begin(await post<Session>('/api/auth/register', { firstName, lastName, email, password, phone: phone || null }))
      navigate('/portal', { replace: true })
    } catch (error) {
      setFormError(applyServerErrors(error, ['firstName', 'lastName', 'email', 'phone', 'password'], form.setError))
    }
  })

  return (
    <AuthLayout
      title="Create your account"
      subtitle="Start your study abroad application with Nicho Tan."
      footer={
        <>
          Already have an account?{' '}
          <Link to="/login" className="font-medium text-primary hover:underline">
            Sign in
          </Link>
        </>
      }
    >
      <form onSubmit={submit} noValidate className="grid gap-4">
        <FormError message={formError} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="First name" required autoComplete="given-name" error={errors.firstName?.message} {...form.register('firstName')} />
          <Field label="Last or family name" required autoComplete="family-name" error={errors.lastName?.message} {...form.register('lastName')} />
        </div>
        <Field label="Email address" required type="email" autoComplete="email" error={errors.email?.message} {...form.register('email')} />
        <Field label="Phone number (include your country code)" type="tel" inputMode="tel" autoComplete="tel" placeholder="+263 77 123 4567" hint={<PhoneHint value={phoneValue} />} error={errors.phone?.message} {...form.register('phone')} />
        <PasswordField label="Password" required autoComplete="new-password" hint={passwordRule} error={errors.password?.message} {...form.register('password')} />
        <PasswordField label="Confirm password" required autoComplete="new-password" error={errors.confirm?.message} {...form.register('confirm')} />
        <p className="text-xs text-muted-foreground">We’ll email you a link to confirm your address. Your details are only used to manage your application.</p>
        <Button type="submit" size="lg" className="h-11 text-base" disabled={isSubmitting}>
          {isSubmitting && <Loader2 className="animate-spin" />}
          Create account
        </Button>
      </form>
    </AuthLayout>
  )
}
