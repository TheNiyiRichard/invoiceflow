import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import AuthProvider from './auth/AuthProvider.jsx'
import { GuestRoute, ProtectedRoute } from './auth/routes.jsx'
import AppLayout from './components/AppLayout.jsx'
import Overview from './pages/Overview.jsx'
import Clients from './pages/Clients.jsx'
import Invoices from './pages/Invoices.jsx'
import InvoiceEditor from './pages/InvoiceEditor.jsx'
import ComingSoon from './pages/ComingSoon.jsx'
import SignIn from './pages/SignIn.jsx'
import Register from './pages/Register.jsx'
import PayPage from './pages/PayPage.jsx'

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          {/* Signed in: every page sits inside the app shell */}
          <Route element={<ProtectedRoute />}>
            <Route element={<AppLayout />}>
              <Route path="/" element={<Overview />} />
              <Route path="/invoices" element={<Invoices />} />
              <Route path="/invoices/new" element={<InvoiceEditor />} />
              <Route path="/invoices/:id" element={<InvoiceEditor />} />
              <Route path="/clients" element={<Clients />} />
              <Route path="/payments" element={<ComingSoon title="Payments" when="Week 6">Every card payment your clients make, with its date and Stripe reference.</ComingSoon>} />
              <Route path="/security" element={<ComingSoon title="Security" when="Week 6">Two-factor sign-in, recovery codes and your signed-in devices.</ComingSoon>} />
            </Route>
          </Route>
          <Route element={<GuestRoute />}>
            <Route path="/sign-in" element={<SignIn />} />
            <Route path="/register" element={<Register />} />
          </Route>
          {/* Public: no sign-in, only a signed link. Outside both guards on purpose. */}
          <Route path="/pay/:token" element={<PayPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}