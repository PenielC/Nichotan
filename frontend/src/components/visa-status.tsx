import { Badge } from '@/components/ui/badge'
import { visaStatusLabel, type VisaStatus } from '@/lib/visa'

const tone = (s: VisaStatus) =>
  s === 'Approved' ? 'default' : s === 'Refused' || s === 'AdditionalInformationRequested' ? 'destructive' : s === 'Withdrawn' ? 'outline' : 'secondary'

export function VisaStatusBadge({ status }: { status: VisaStatus }) {
  return <Badge variant={tone(status)}>{visaStatusLabel[status]}</Badge>
}
