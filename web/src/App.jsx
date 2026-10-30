import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import Home from './pages/Home.jsx'
import SignIn from './pages/SignIn.jsx'
import Register from './pages/Register.jsx'
import './App.css'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/sign-in" element={<SignIn />} />
        <Route path="/register" element={<Register />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}