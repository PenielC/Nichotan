import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError } from '@/components/form'
import { applyServerErrors } from '@/lib/form-errors'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { get, put, refreshSession, type CandidateProfile } from '@/lib/api'
import { PhoneHint } from '@/components/phone-hint'
import { personName, phone } from '@/lib/schemas'

const schema = z.object({ firstName: personName('first name'), lastName: personName('last name'), phone })
type Values = z.infer<typeof schema>

export default function Profile() {
  const profile = useQuery({ queryKey: ['candidates', 'me'], queryFn: () => get<CandidateProfile>('/api/candidates/me') })

  return (
    <div className="grid gap-6">
      <div>
        <h1 className="text-2xl font-bold text-ink">My details</h1>
        <p className="mt-1 text-muted-foreground">The details your counsellor uses to contact you.</p>
      </div>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg text-ink">Personal information</CardTitle>
          <CardDescription>Use your names exactly as they appear in your passport.</CardDescription>
        </CardHeader>
        <CardContent>
          {profile.data ? <ProfileForm profile={profile.data} /> : profile.isError ? <FormError message="We couldn’t load your details. Please refresh the page." /> : <Skeleton className="h-64" />}
        </CardContent>
      </Card>
    </div>
  )
}

function ProfileForm({ profile }: { profile: CandidateProfile }) {
  const queryClient = useQueryClient()
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { firstName: profile.firstName, lastName: profile.lastName, phone: profile.phone ?? '' } })
  const phoneValue = useWatch({ control: form.control, name: 'phone' })
  const { errors, isSubmitting, isDirty } = form.formState

  const submit = form.handleSubmit(async (values) => {
    setFormError(null)
    try {
      const saved = await put<CandidateProfile>('/api/candidates/me', { ...values, phone: values.phone || null })
      queryClient.setQueryData(['candidates', 'me'], saved)
      form.reset({ firstName: saved.firstName, lastName: saved.lastName, phone: saved.phone ?? '' })
      // The greeting and menus read the name from the session.
      await refreshSession()
      toast.success('Your details are saved.')
    } catch (error) {
      setFormError(applyServerErrors(error, ['firstName', 'lastName', 'phone'], form.setError))
    }
  })

  return (
    <form onSubmit={submit} noValidate className="grid max-w-2xl gap-4">
      <FormError message={formError} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="First name" required autoComplete="given-name" error={errors.firstName?.message} {...form.register('firstName')} />
        <Field label="Last or family name" required autoComplete="family-name" error={errors.lastName?.message} {...form.register('lastName')} />
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Email address" value={profile.email} readOnly disabled hint="Contact your counsellor to change your email address." />
        <Field label="Phone number (include your country code)" type="tel" inputMode="tel" autoComplete="tel" placeholder="+263 77 123 4567" hint={<PhoneHint value={phoneValue} />} error={errors.phone?.message} {...form.register('phone')} />
      </div>
      <Button type="submit" className="h-10 justify-self-start px-5" disabled={isSubmitting || !isDirty}>
        {isSubmitting && <Loader2 className="animate-spin" />}
        Save changes
      </Button>
    </form>
  )
}
