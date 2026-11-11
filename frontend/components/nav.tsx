"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";
import { BarChart3, FileText, FlaskConical, Info, ShieldAlert, Trophy, Database } from "lucide-react";

const items = [
  { href: "/", label: "Dashboard", icon: BarChart3 },
  { href: "/tests", label: "Test Cases", icon: FlaskConical },
  { href: "/security", label: "Security", icon: ShieldAlert },
  { href: "/rag", label: "RAG", icon: Database },
  { href: "/leaderboard", label: "Leaderboard", icon: Trophy },
  { href: "/reports", label: "Reports", icon: FileText },
  { href: "/about", label: "About", icon: Info },
];

export function Nav() {
  const path = usePathname();
  return (
    <aside className="border-b md:min-h-screen md:w-56 md:border-b-0 md:border-r bg-card p-4">
      <p className="mb-4 text-sm font-bold text-primary">LLM Quality Evaluator</p>
      <nav className="flex gap-1 overflow-x-auto md:flex-col" aria-label="Main">
        {items.map(({ href, label, icon: Icon }) => (
          <Link
            key={href}
            href={href}
            className={cn(
              "flex items-center gap-2 whitespace-nowrap rounded-md px-3 py-2 text-sm hover:bg-muted",
              (href === "/" ? path === "/" : path.startsWith(href)) && "bg-muted font-semibold",
            )}
          >
            <Icon className="h-4 w-4" /> {label}
          </Link>
        ))}
      </nav>
    </aside>
  );
}
