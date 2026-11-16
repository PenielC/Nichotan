import { createBrowserRouter } from 'react-router-dom'
import { AccountShell, GuestOnly, NotFound, RequireCandidate, RequireSignIn, RequireStaff } from '@/components/guards'
import PortalLayout from '@/layouts/PortalLayout'
import StaffLayout from '@/layouts/StaffLayout'
import { caseWorkerRoles, reportRoles } from '@/lib/auth'
import Security from '@/pages/account/Security'
import Login from '@/pages/auth/Login'
import Register from '@/pages/auth/Register'
import { AcceptInvitation, ForgotPassword, ResetPassword, VerifyEmail } from '@/pages/auth/TokenPages'
import Landing from '@/pages/Landing'
import Application from '@/pages/portal/Application'
import Applications from '@/pages/portal/Applications'
import Documents from '@/pages/portal/Documents'
import PortalHome from '@/pages/portal/PortalHome'
import Profile from '@/pages/portal/Profile'
import { AdmissionList, AdmissionPage } from '@/pages/staff/Admissions'
import { VisaList, VisaPage } from '@/pages/staff/Visas'
import Reports from '@/pages/staff/Reports'
import Tasks from '@/pages/staff/Tasks'
import { ApplicationList } from '@/pages/staff/Applications'
import AuditLog from '@/pages/staff/AuditLog'
import { CandidateDetail, CandidateList } from '@/pages/staff/Candidates'
import Dashboard from '@/pages/staff/Dashboard'
import { Checklists } from '@/pages/staff/Checklists'
import { Directory } from '@/pages/staff/Directory'
import { EnquiryList } from '@/pages/staff/Enquiries'
import { ReviewQueue } from '@/pages/staff/Documents'
import Messages from '@/pages/staff/Messages'
import Team from '@/pages/staff/Team'

export const router = createBrowserRouter([
  { path: '/', element: <Landing /> },
  {
    element: <GuestOnly />,
    children: [
      { path: '/login', element: <Login /> },
      { path: '/register', element: <Register /> },
      { path: '/forgot-password', element: <ForgotPassword /> },
    ],
  },
  // Links from emails work whether or not someone is signed in.
  { path: '/verify-email', element: <VerifyEmail /> },
  { path: '/reset-password', element: <ResetPassword /> },
  { path: '/accept-invitation', element: <AcceptInvitation /> },
  {
    element: <RequireSignIn />,
    children: [
      {
        element: <RequireCandidate />,
        children: [
          {
            path: '/portal',
            element: <PortalLayout />,
            children: [
              { index: true, element: <PortalHome /> },
              { path: 'profile', element: <Profile /> },
              { path: 'application', element: <Application /> },
              { path: 'applications', element: <Applications /> },
              { path: 'documents', element: <Documents /> },
            ],
          },
        ],
      },
      {
        path: '/staff',
        element: (
          <RequireStaff>
            <StaffLayout />
          </RequireStaff>
        ),
        children: [
          { index: true, element: <Dashboard /> },
          {
            element: <RequireStaff roles={caseWorkerRoles} />,
            children: [
              { path: 'tasks', element: <Tasks /> },
              { path: 'candidates', element: <CandidateList /> },
              { path: 'candidates/:id', element: <CandidateDetail /> },
              { path: 'applications', element: <ApplicationList /> },
              { path: 'admissions', element: <AdmissionList /> },
              { path: 'admissions/:id', element: <AdmissionPage /> },
              { path: 'visa', element: <VisaList /> },
              { path: 'visa/:id', element: <VisaPage /> },
              { path: 'directory', element: <Directory /> },
              { path: 'documents', element: <ReviewQueue /> },
              { path: 'enquiries', element: <EnquiryList /> },
            ],
          },
          {
            element: <RequireStaff roles={reportRoles} />,
            children: [{ path: 'reports', element: <Reports /> }],
          },
          {
            element: <RequireStaff roles={['Administrator']} />,
            children: [
              { path: 'team', element: <Team /> },
              { path: 'checklists', element: <Checklists /> },
              { path: 'audit', element: <AuditLog /> },
              { path: 'messages', element: <Messages /> },
            ],
          },
        ],
      },
      { path: '/account', element: <AccountShell />, children: [{ path: 'security', element: <Security /> }] },
    ],
  },
  { path: '*', element: <NotFound /> },
])
