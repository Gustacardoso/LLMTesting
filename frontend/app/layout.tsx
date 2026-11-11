import type { Metadata } from "next";
import "./globals.css";
import { Nav } from "@/components/nav";

export const metadata: Metadata = {
  title: "LLM Quality Evaluator",
  description: "Testing and validation platform for generative AI",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <div className="flex min-h-screen flex-col md:flex-row">
          <Nav />
          <main className="flex-1 space-y-6 p-4 md:p-8">{children}</main>
        </div>
      </body>
    </html>
  );
}
