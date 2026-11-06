import { Outlet, useNavigate } from "react-router";
import { useAuth } from "@/auth/useAuth";
import { Button } from "@/components/ui/button";

export function AppLayout() {
  const { account, signOut } = useAuth();
  const navigate = useNavigate();

  async function onSignOut() {
    await signOut();
    navigate("/sign-in", { replace: true });
  }

  return (
    <div className="min-h-svh bg-background">
      <header className="border-b">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 p-4">
          <div>
            <p className="font-semibold">ElderSafe</p>
            <p className="text-xs text-muted-foreground">
              Portfolio project. Not a medical device. Synthetic data only.
            </p>
          </div>

          <div className="flex items-center gap-3">
            <span className="text-sm text-muted-foreground">
              {account?.displayName}
            </span>
            <Button variant="outline" size="sm" onClick={onSignOut}>
              Sign out
            </Button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-5xl p-4">
        <Outlet />
      </main>
    </div>
  );
}
