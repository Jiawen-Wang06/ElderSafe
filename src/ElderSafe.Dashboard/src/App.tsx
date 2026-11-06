import { Navigate, Route, Routes } from "react-router";
import { useAuth } from "@/auth/AuthContext";
import { RequireAuth } from "@/auth/RequireAuth";
import { AppLayout } from "@/AppLayout";
import { SignInPage } from "@/pages/SignInPage";
import { RegisterPage } from "@/pages/RegisterPage";
import { CaregiverAlertsPage } from "@/pages/CaregiverAlertsPage";
import { ResidentSettingsPage } from "@/pages/ResidentSettingsPage";

/**
 * Sends each account to the screen that fits it.
 *
 * A resident account configures; a caregiver account watches. Both screens
 * remain reachable by URL, because the API decides what each account may
 * actually see and the routing is only a convenience.
 */
function Home() {
  const { account } = useAuth();

  if (account?.accountType === "Resident") {
    return <Navigate to="/settings" replace />;
  }

  return <Navigate to="/alerts" replace />;
}

export default function App() {
  return (
    <Routes>
      <Route path="/sign-in" element={<SignInPage />} />
      <Route path="/register" element={<RegisterPage />} />

      <Route
        element={
          <RequireAuth>
            <AppLayout />
          </RequireAuth>
        }
      >
        <Route path="/" element={<Home />} />
        <Route path="/alerts" element={<CaregiverAlertsPage />} />
        <Route path="/settings" element={<ResidentSettingsPage />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
