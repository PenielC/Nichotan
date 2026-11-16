import { Badge } from '@/components/ui/badge'
import { statusLabel, type AdmissionStatus } from '@/lib/admissions'

const tone = (s: AdmissionStatus) =>
  s === 'OfferReceived' ? 'default' : s === 'Rejected' || s === 'MoreInformationRequired' || s === 'ResubmissionRequired' ? 'destructive' : s === 'Withdrawn' || s === 'Closed' ? 'outline' : 'secondary'

export function AdmissionStatusBadge({ status, label }: { status: AdmissionStatus; label?: string }) {
  return <Badge variant={tone(status)}>{label ?? statusLabel[status]}</Badge>
}
