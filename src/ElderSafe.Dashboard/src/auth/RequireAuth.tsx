import { Navigate, useLocation } from "react-router";
import type { ReactNode } from "react";
import { useAuth } from "@/auth/useAuth";

/**
 * Keeps unauthenticated visitors off a route.
 *
 * This is convenience, not security. The API refuses every request without a
 * session regardless of what the browser shows, so a visitor who edits the
 * URL reaches an empty screen rather than somebody's data.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { account, loading } = useAuth();
  const location = useLocation();

  if (loading) {
    return (
      <div className="flex min-h-svh items-center justify-center text-muted-foreground">
        Loading…
      </div>
    );
  }

  if (account === null) {
    return <Navigate to="/sign-in" state={{ from: location.pathname }} replace />;
  }

  return <>{children}</>;
}
