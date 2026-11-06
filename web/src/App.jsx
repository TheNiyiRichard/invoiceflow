import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import AuthProvider from './auth/AuthProvider.jsx'
import { GuestRoute, ProtectedRoute } from './auth/routes.jsx'
import AppLayout from './components/AppLayout.jsx'
import Overview from './pages/Overview.jsx'
import Clients from './pages/Clients.jsx'
import ComingSoon from './pages/ComingSoon.jsx'
import SignIn from './pages/SignIn.jsx'
import Register from './pages/Register.jsx'

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          {/* Signed in: every page sits inside the app shell */}
          <Route element={<ProtectedRoute />}>
            <Route element={<AppLayout />}>
              <Route path="/" element={<Overview />} />
              <Route path="/invoices" element={<ComingSoon title="Invoices" when="Day 19">Every invoice, with status tabs and paging.</ComingSoon>} />
              <Route path="/clients" element={<Clients />} />
              <Route path="/payments" element={<ComingSoon title="Payments" when="Week 5">Card payments through Stripe, in test mode.</ComingSoon>} />
              <Route path="/security" element={<ComingSoon title="Security" when="Week 6">Two-factor sign-in, recovery codes and your signed-in devices.</ComingSoon>} />
            </Route>
          </Route>
          <Route element={<GuestRoute />}>
            <Route path="/sign-in" element={<SignIn />} />
            <Route path="/register" element={<Register />} />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}