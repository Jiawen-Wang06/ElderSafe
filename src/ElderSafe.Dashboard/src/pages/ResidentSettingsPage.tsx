import { useAuth } from "@/auth/useAuth";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

export function ResidentSettingsPage() {
  const { account } = useAuth();
  const resident = account?.residents[0] ?? null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>Settings</CardTitle>
        <CardDescription>
          {resident === null
            ? "This account does not hold a resident's data."
            : `Configuration for ${resident.displayName}`}
        </CardDescription>
      </CardHeader>

      <CardContent className="text-sm text-muted-foreground">
        <p>
          Choosing who receives alerts, how far an unanswered alert travels and
          whether it may end in an emergency call is the next piece of work.
        </p>
      </CardContent>
    </Card>
  );
}
