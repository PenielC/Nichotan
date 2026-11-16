import { useMutation } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { ApiError, post } from '@/lib/api'

export function ResendVerification() {
  const resend = useMutation({
    mutationFn: () => post('/api/auth/resend-verification'),
    onSuccess: () => toast.success('A new confirmation link is on its way.'),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'We couldn’t send the link. Please try again.'),
  })
  return (
    <Button variant="outline" onClick={() => resend.mutate()} disabled={resend.isPending || resend.isSuccess}>
      {resend.isPending && <Loader2 className="animate-spin" />}
      {resend.isSuccess ? 'Link sent' : 'Resend link'}
    </Button>
  )
}
