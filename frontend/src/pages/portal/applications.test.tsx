import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import Applications from './Applications'

const fetchMock = vi.fn<typeof fetch>()
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const application = {
  id: 'app-1', institutionName: 'Test University', programmeName: 'Computer Science', country: 'Canada', intakeTerm: 'Fall', intakeYear: 2027,
  status: 'Draft', statusChangedAtUtc: '2026-11-20T10:00:00Z', submittedToInstitutionOn: null, steps: [], canSend: false, canWithdraw: true,
  blockers: ['Upload Passport'], offer: null, acceptance: null, canUploadOfferLetter: false, offerLetter: null, offerLetterTypeId: null,
}

/** Answers each URL the page asks for. Anything it does not know about (the visa widgets) gets an empty list. */
function serve(applications: unknown[] = []) {
  fetchMock.mockImplementation(async (input) => (String(input) === '/api/admissions/mine' ? json(200, applications) : json(200, [])))
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/portal/applications']}>
        <Routes>
          <Route path="/portal/applications" element={<Applications />} />
          <Route path="/portal/application" element={<p>The application form page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => vi.stubGlobal('fetch', fetchMock))
afterEach(() => {
  fetchMock.mockReset()
  vi.unstubAllGlobals()
})

describe('Add an application (student)', () => {
  it('goes straight to the application form: there is no modal', async () => {
    serve()
    renderPage()
    const add = (await screen.findAllByRole('link', { name: /Add an application/ }))[0]
    expect(add).toHaveAttribute('href', '/portal/application')

    await userEvent.click(add)
    expect(await screen.findByText('The application form page')).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('is a link, not a button that opens a form of its own', async () => {
    serve([application])
    renderPage()
    await screen.findByText('Test University')
    expect(screen.queryByRole('button', { name: /Add an application/ })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Add an application/ })).toBeInTheDocument()
  })

  it('explains that the form holds the country, program and start date, and that the counsellor adds the applications', async () => {
    serve([application])
    renderPage()
    expect(await screen.findByText(/start with your application form: choose your country, program and start date there/)).toBeInTheDocument()
    expect(screen.getByText(/Your counsellor then adds your applications here/)).toBeInTheDocument()
  })

  it('sends a student with no applications to the form, in words about the form', async () => {
    serve()
    renderPage()
    expect(await screen.findByText(/You don’t have any applications yet/)).toBeInTheDocument()
    expect(screen.getByText(/Fill in and sign your application form first/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to my form' })).toHaveAttribute('href', '/portal/application')
  })

  it('never asks for a school, a programme, a country or a start date on this page', async () => {
    serve()
    renderPage()
    await screen.findByText(/You don’t have any applications yet/)
    expect(screen.queryByLabelText(/School/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Programme/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Country/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/Preferred Program of Study/)).not.toBeInTheDocument()
  })

  it('shows the applications the counsellor has added, and no longer fetches or sends any "request"', async () => {
    serve([application])
    renderPage()
    expect(await screen.findByText('Test University')).toBeInTheDocument()
    expect(screen.getByText(/Computer Science · Canada · Fall 2027/)).toBeInTheDocument()
    expect(screen.getByText('Upload Passport', { exact: false })).toBeInTheDocument()

    const urls = fetchMock.mock.calls.map(([input]) => String(input))
    expect(urls.some((u) => u.includes('/requests'))).toBe(false)
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
  })
})
