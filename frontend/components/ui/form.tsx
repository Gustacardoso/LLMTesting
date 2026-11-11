import * as React from "react";
import { cn } from "@/lib/utils";

const base = "w-full rounded-md border bg-card px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

export const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  ({ className, ...p }, ref) => <input ref={ref} className={cn(base, className)} {...p} />,
);
Input.displayName = "Input";

export const Textarea = React.forwardRef<HTMLTextAreaElement, React.TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ className, ...p }, ref) => <textarea ref={ref} rows={3} className={cn(base, className)} {...p} />,
);
Textarea.displayName = "Textarea";

export const Select = ({ className, ...p }: React.SelectHTMLAttributes<HTMLSelectElement>) => (
  <select className={cn(base, className)} {...p} />
);

export const Label = ({ className, ...p }: React.LabelHTMLAttributes<HTMLLabelElement>) => (
  <label className={cn("mb-1 block text-xs font-medium text-muted-foreground", className)} {...p} />
);

export function Badge({ ok, children }: { ok?: boolean | null; children: React.ReactNode }) {
  const tone = ok == null ? "bg-muted text-muted-foreground" : ok ? "bg-success/15 text-success" : "bg-danger/15 text-danger";
  return <span className={cn("inline-flex rounded-full px-2 py-0.5 text-xs font-medium", tone)}>{children}</span>;
}

export function ErrorBox({ message }: { message: string | null }) {
  return message ? (
    <p role="alert" className="rounded-md border border-danger/40 bg-danger/10 p-2 text-sm text-danger">{message}</p>
  ) : null;
}
