import { Outlet, useNavigate } from "react-router";
import { useAuth } from "@/auth/useAuth";
import { Button } from "@/components/ui/button";
import headerArt from "@/assets/header.png";

export function AppLayout() {
  const { account, signOut } = useAuth();
  const navigate = useNavigate();

  async function onSignOut() {
    await signOut();
    navigate("/sign-in", { replace: true });
  }

  return (
    <div className="min-h-svh bg-background">
      <header className="mx-auto max-w-5xl px-4 pt-4">
        <div className="relative overflow-hidden rounded-xl">
          <img
            src={headerArt}
            alt="ElderSafe — look out, care more"
            className="block w-full"
          />

          <div className="absolute inset-x-0 top-0 flex items-center justify-end gap-2 p-3 sm:p-4">
            <span className="hidden rounded-full bg-background/70 px-3 py-1 text-sm font-medium text-foreground/80 backdrop-blur-sm sm:inline">
              {account?.displayName}
            </span>
            <Button
              variant="secondary"
              size="sm"
              className="rounded-full shadow-sm"
              onClick={onSignOut}
            >
              Sign out
            </Button>
          </div>
        </div>
      </header>

      <p className="mx-auto max-w-5xl px-4 pt-2 text-xs text-muted-foreground">
        Portfolio project. Not a medical device. Synthetic data only.
      </p>

      <main className="mx-auto max-w-5xl p-4">
        <Outlet />
      </main>
    </div>
  );
}
