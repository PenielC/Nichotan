import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { MessageCircle } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { ApiError, get, post, type CandidateProfile } from '@/lib/api'
import { useAuth } from '@/lib/auth'
import { listMessage, whatsappLink, whatsappNumber } from '@/lib/whatsapp'

export type ShareTopic = 'documents' | 'visa-documents' | 'pre-departure'

/**
 * Opens the staff member's own WhatsApp with a ready-written message listing the items and a link to the portal. Nothing is
 * sent by the system; the staff member presses send. Opening it is recorded in the audit log, because a student's list is going
 * to an outside service.
 */
export function WhatsAppShareButton({ candidateId, topic, intro, lines, portalPath, label = 'Send on WhatsApp' }: { candidateId: string; topic: ShareTopic; intro: string; lines: string[]; portalPath: string; label?: string }) {
  const queryClient = useQueryClient()
  const { state } = useAuth()
  const staffFirstName = state.status === 'signedIn' ? state.session.user.firstName : 'your counsellor'
  const [busy, setBusy] = useState(false)

  const share = async () => {
    // The window is opened straight away (a browser only allows that from a click); it is pointed at WhatsApp once we know the number.
    const target = window.open('about:blank', '_blank')
    setBusy(true)
    try {
      const profile = await queryClient.fetchQuery({ queryKey: ['candidates', candidateId], queryFn: () => get<CandidateProfile>(`/api/candidates/${candidateId}`), staleTime: 0 })
      const number = whatsappNumber(profile.phone)
      const text = listMessage({ firstName: profile.firstName, staffFirstName, intro, lines, link: `${window.location.origin}${portalPath}` })
      const url = whatsappLink(number, text)
      if (target) target.location.href = url
      else window.location.href = url
      if (!number) toast.message('No phone number with a country code is saved for this student, so WhatsApp will ask who to send it to.')
      void post(`/api/candidates/${candidateId}/whatsapp-shares`, { topic }).catch(() => undefined)
    } catch (e) {
      target?.close()
      toast.error(e instanceof ApiError ? e.message : 'We couldn’t open WhatsApp. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Button variant="outline" size="sm" onClick={() => void share()} disabled={busy || lines.length === 0} title={lines.length === 0 ? 'There is nothing to send yet' : 'Opens WhatsApp with the message ready to send'}>
      <MessageCircle /> {label}
    </Button>
  )
}
