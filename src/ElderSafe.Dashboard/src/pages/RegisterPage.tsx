import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { api, ApiError } from "@/lib/api";
import type { AccountType } from "@/lib/api";
import { useAuth } from "@/auth/useAuth";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/PasswordInput";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

export function RegisterPage() {
  const { signIn } = useAuth();
  const navigate = useNavigate();

  const [userName, setUserName] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [accountType, setAccountType] = useState<AccountType>("Caregiver");
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setErrors([]);
    setBusy(true);

    try {
      await api.register(userName, password, displayName, accountType);
      await signIn(userName, password);
      navigate("/", { replace: true });
    } catch (error) {
      if (error instanceof ApiError && error.details.length > 0) {
        setErrors(error.details);
      } else {
        setErrors(["That account could not be created."]);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-svh items-center justify-center p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>Create an account</CardTitle>
          <CardDescription>
            Registering gives you an account and nothing else. A caregiver sees
            no residents until one assigns them.
          </CardDescription>
        </CardHeader>

        <CardContent>
          <form onSubmit={onSubmit} className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <label htmlFor="accountType" className="text-sm font-medium">
                This account is for
              </label>
              <select
                id="accountType"
                className="h-9 rounded-md border border-input bg-transparent px-3 text-sm"
                value={accountType}
                onChange={(event) => setAccountType(event.target.value as AccountType)}
              >
                <option value="Caregiver">A caregiver</option>
                <option value="Resident">A resident</option>
              </select>
              {accountType === "Resident" && (
                <p className="text-xs text-muted-foreground">
                  A resident account holds one person&apos;s data and decides who
                  receives their alerts. It is intended to be set up by a
                  caregiver or family member on the resident&apos;s behalf.
                </p>
              )}
            </div>

            <div className="flex flex-col gap-2">
              <label htmlFor="displayName" className="text-sm font-medium">
                Name
              </label>
              <Input
                id="displayName"
                required
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
              />
            </div>

            <div className="flex flex-col gap-2">
              <label htmlFor="userName" className="text-sm font-medium">
                Email
              </label>
              <Input
                id="userName"
                type="email"
                autoComplete="username"
                required
                value={userName}
                onChange={(event) => setUserName(event.target.value)}
              />
            </div>

            <div className="flex flex-col gap-2">
              <label htmlFor="password" className="text-sm font-medium">
                Password
              </label>
              <PasswordInput
                id="password"
                autoComplete="new-password"
                required
                minLength={12}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
              <p className="text-xs text-muted-foreground">At least 12 characters.</p>
            </div>

            {errors.length > 0 && (
              <ul role="alert" className="flex flex-col gap-1 text-sm text-destructive">
                {errors.map((message) => (
                  <li key={message}>{message}</li>
                ))}
              </ul>
            )}

            <Button type="submit" disabled={busy}>
              {busy ? "Creating…" : "Create account"}
            </Button>

            <p className="text-sm text-muted-foreground">
              Already registered? <Link to="/sign-in" className="underline">Sign in</Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
