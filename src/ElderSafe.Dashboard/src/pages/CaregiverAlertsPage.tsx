import { useEffect, useState } from "react";
import { useAuth } from "@/auth/AuthContext";
import { createAlertsConnection } from "@/lib/signalr";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

type ConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

export function CaregiverAlertsPage() {
  const { account } = useAuth();
  const [state, setState] = useState<ConnectionState>("connecting");

  useEffect(() => {
    const connection = createAlertsConnection();

    connection.onreconnecting(() => setState("reconnecting"));
    connection.onreconnected(() => setState("connected"));
    connection.onclose(() => setState("disconnected"));

    connection
      .start()
      .then(() => setState("connected"))
      .catch(() => setState("disconnected"));

    return () => {
      void connection.stop();
    };
  }, []);

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle>Alerts</CardTitle>
          <CardDescription>
            Live connection: {state}
          </CardDescription>
        </CardHeader>

        <CardContent className="text-sm text-muted-foreground">
          <p>
            The alert list, acknowledgement and the simulate control are the
            next piece of work. This screen currently proves only that the
            live connection is established.
          </p>

          {account !== null && account.residents.length === 0 && (
            <p className="mt-2">
              You are not assigned to any residents yet. A resident account has
              to assign you before you can see their alerts.
            </p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
