import { useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import { Input } from "@/components/ui/input";

type PasswordInputProps = Omit<React.ComponentProps<"input">, "type">;

/**
 * A password field the person can choose to reveal.
 *
 * Worth having here in particular: sign-in failures are deliberately vague,
 * because distinguishing a wrong password from an unknown username would turn
 * the form into a way of discovering who holds an account. That is the right
 * trade for security and a poor one for someone who simply mistyped, so being
 * able to see what was typed is how the person tells the two apart.
 */
export function PasswordInput(props: PasswordInputProps) {
  const [visible, setVisible] = useState(false);

  return (
    <div className="relative">
      <Input {...props} type={visible ? "text" : "password"} className="pr-10" />

      <button
        type="button"
        onClick={() => setVisible((shown) => !shown)}
        className="absolute inset-y-0 right-0 flex items-center px-3 text-muted-foreground transition-colors hover:text-foreground"
        aria-label={visible ? "Hide password" : "Show password"}
        aria-pressed={visible}
        title={visible ? "Hide password" : "Show password"}
      >
        {visible ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
      </button>
    </div>
  );
}
