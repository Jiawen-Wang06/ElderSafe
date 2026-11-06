import { createContext, useContext } from "react";
import type { CurrentAccount } from "@/lib/api";

export interface AuthValue {
  account: CurrentAccount | null;
  loading: boolean;
  signIn: (userName: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
  refresh: () => Promise<void>;
}

export const AuthContext = createContext<AuthValue | null>(null);

/**
 * Reads who is signed in.
 *
 * In its own module rather than beside the provider, because a file that
 * exports both a component and something else breaks Vite's fast refresh:
 * editing the hook would force a full reload and discard the state of
 * whatever screen was open. Splitting them is the fix the warning asks for.
 */
export function useAuth(): AuthValue {
  const value = useContext(AuthContext);

  if (value === null) {
    throw new Error("useAuth must be used inside an AuthProvider.");
  }

  return value;
}
