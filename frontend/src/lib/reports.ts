export interface StatusCount {
  status: string
  count: number
}

export interface Conversion {
  name: string
  definition: string
  cohort: number
  succeeded: number
  declined: number
  pending: number
}

/** The monthly report: counts only. Pipeline = where records stood; movement = what happened in the month. */
export interface MonthlyReport {
  month: string
  from: string
  to: string
  asAt: string
  admissionPipeline: StatusCount[]
  admissionMovement: StatusCount[]
  visaPipeline: StatusCount[]
  visaMovement: StatusCount[]
  conversions: Conversion[]
  activity: { registrations: number; enquiries: number; acceptancesRecorded: number }
}
