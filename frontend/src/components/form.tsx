import { useId, useState, type ComponentProps, type ReactNode } from 'react'
import { AlertCircle, Eye, EyeOff } from 'lucide-react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

type FieldProps = ComponentProps<typeof Input> & { label: string; error?: string; hint?: ReactNode; required?: boolean }

/** A labelled input with its error message linked for screen readers. */
export function Field({ label, error, hint, required, className, id, ...props }: FieldProps) {
  const generated = useId()
  const inputId = id ?? generated
  return (
    <div className={cn('grid gap-1.5', className)}>
      <Label htmlFor={inputId}>
        {label}
        {required && <span className="text-destructive" aria-hidden>*</span>}
      </Label>
      <Input id={inputId} aria-invalid={error ? true : undefined} aria-describedby={error ? `${inputId}-error` : undefined} className="h-10 bg-card" {...props} />
      {hint && !error && <p className="text-xs text-muted-foreground">{hint}</p>}
      {error && (
        <p id={`${inputId}-error`} className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}

type SelectFieldProps = ComponentProps<'select'> & { label: string; error?: string; required?: boolean; options: { value: string; label: string }[]; placeholder?: string }

/** A labelled drop-down list (the browser's own, so it works well on phones). */
export function SelectField({ label, error, required, options, placeholder = 'Select…', className, id, ...props }: SelectFieldProps) {
  const generated = useId()
  const selectId = id ?? generated
  return (
    <div className={cn('grid gap-1.5', className)}>
      <Label htmlFor={selectId}>
        {label}
        {required && <span className="text-destructive" aria-hidden>*</span>}
      </Label>
      <select
        id={selectId}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${selectId}-error` : undefined}
        className="h-10 w-full rounded-lg border border-input bg-card px-2.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive"
        {...props}
      >
        <option value="">{placeholder}</option>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      {error && (
        <p id={`${selectId}-error`} className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}

export function PasswordField(props: Omit<FieldProps, 'type'>) {
  const [visible, setVisible] = useState(false)
  return (
    <div className="relative">
      <Field {...props} type={visible ? 'text' : 'password'} className={cn('[&_input]:pr-10', props.className)} />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        className="absolute top-[1.85rem] right-1 flex size-8 items-center justify-center rounded-md text-muted-foreground hover:text-foreground"
        aria-label={visible ? 'Hide password' : 'Show password'}
      >
        {visible ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
      </button>
    </div>
  )
}

export function FormError({ message }: { message?: string | null }) {
  if (!message) return null
  return (
    <Alert variant="destructive" role="alert">
      <AlertCircle />
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  )
}
