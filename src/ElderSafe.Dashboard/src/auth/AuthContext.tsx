import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { api } from "@/lib/api";
import type { CurrentAccount } from "@/lib/api";
import { AuthContext } from "@/auth/useAuth";

/**
 * Holds who is signed in.
 *
 * The session lives in an HttpOnly cookie, so this never sees or stores a
 * token: an XSS bug cannot read a session it has no access to. "Who am I" is
 * therefore a question for the server, asked on load and after signing in,
 * rather than something kept in browser storage.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [account, setAccount] = useState<CurrentAccount | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    try {
      setAccount(await api.me());
    } catch {
      // 401 is the ordinary answer for a visitor who is not signed in.
      setAccount(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    // The one place this application genuinely fetches on mount: the session
    // is a cookie the client cannot read, so the first render has no way of
    // knowing who is signed in without asking. Every setState inside refresh
    // happens after an await, so no render cascades from the effect body —
    // but the rule stops at the call boundary and cannot see that. The
    // alternatives are a data-fetching library or a router loader, both of
    // which are more machinery than one question deserves.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void refresh();
  }, [refresh]);

  const signIn = useCallback(
    async (userName: string, password: string) => {
      await api.signIn(userName, password);
      await refresh();
    },
    [refresh],
  );

  const signOut = useCallback(async () => {
    await api.signOut();
    setAccount(null);
  }, []);

  const value = useMemo(
    () => ({ account, loading, signIn, signOut, refresh }),
    [account, loading, signIn, signOut, refresh],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
