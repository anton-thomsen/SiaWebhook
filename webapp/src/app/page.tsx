import { redirect } from "next/navigation";
import { apiFetch } from "@/lib/api";
import type { SiaSettings } from "@/lib/types";
import { SettingsForm } from "./settings-form";

// Always fetch fresh settings on each request — they change at runtime.
export const dynamic = "force-dynamic";

export default async function Home() {
  const res = await apiFetch("/api/settings");

  // Cookie missing/expired or rejected by the API → back to login.
  if (res.status === 401) redirect("/login");

  const settings: SiaSettings = await res.json();

  return (
    <main className="mx-auto w-full max-w-2xl flex-1 p-6">
      <SettingsForm initial={settings} />
    </main>
  );
}
