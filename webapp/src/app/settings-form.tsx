"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import type { SiaSettings, SiaSettingsUpdate } from "@/lib/types";

type Status = { kind: "success" | "error"; text: string } | null;

// Characters with structural meaning in a SIA DC-09 frame — rejected server-side too.
const RESERVED = /[\r\n[\]|_]/;

export function SettingsForm({ initial }: { initial: SiaSettings }) {
  const router = useRouter();

  const [form, setForm] = useState<SiaSettingsUpdate>({
    accountNumber: initial.accountNumber,
    receiverNumber: initial.receiverNumber,
    accountPrefix: initial.accountPrefix,
    eventCode: initial.eventCode,
  });
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [saveStatus, setSaveStatus] = useState<Status>(null);
  const [testStatus, setTestStatus] = useState<Status>(null);

  function update<K extends keyof SiaSettingsUpdate>(key: K, value: SiaSettingsUpdate[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  function validate(): string | null {
    for (const [label, val] of [
      ["Account number", form.accountNumber],
      ["Receiver number", form.receiverNumber],
      ["Account prefix", form.accountPrefix],
      ["Event code", form.eventCode],
    ] as const) {
      if (!val.trim()) return `${label} is required.`;
      if (RESERVED.test(val)) return `${label} contains a reserved SIA delimiter.`;
    }
    return null;
  }

  async function onSave(e: React.FormEvent) {
    e.preventDefault();
    setSaveStatus(null);
    const err = validate();
    if (err) {
      setSaveStatus({ kind: "error", text: err });
      return;
    }
    setSaving(true);

    const payload: SiaSettingsUpdate = form;
    const res = await fetch("/api/settings", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    setSaving(false);

    if (res.status === 401) {
      router.replace("/login");
      return;
    }
    if (res.ok) {
      setSaveStatus({ kind: "success", text: "Settings saved." });
      router.refresh();
    } else {
      const data = await res.json().catch(() => null);
      setSaveStatus({ kind: "error", text: data?.error ?? "Save failed." });
    }
  }

  async function onTest() {
    setTesting(true);
    setTestStatus(null);
    const res = await fetch("/api/settings/test-connection", { method: "POST" });
    setTesting(false);

    if (res.status === 401) {
      router.replace("/login");
      return;
    }
    const data = await res.json().catch(() => null);
    if (data?.success) {
      setTestStatus({ kind: "success", text: "Connection successful." });
    } else {
      setTestStatus({ kind: "error", text: data?.error ?? "Connection failed." });
    }
  }

  async function onLogout() {
    await fetch("/api/logout", { method: "POST" });
    router.replace("/login");
    router.refresh();
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">SIA Alarm Configuration</h1>
        <button
          type="button"
          onClick={onLogout}
          className="rounded-md border border-black/15 px-3 py-1.5 text-sm hover:bg-black/5 dark:border-white/15 dark:hover:bg-white/5"
        >
          Log out
        </button>
      </div>

      <form onSubmit={onSave} className="space-y-5">
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-3">
          <ReadOnlyField label="IP Address" value={initial.ipAddress} />
          <ReadOnlyField label="Port" value={String(initial.port)} />
          <ReadOnlyField
            label="Encryption"
            value={initial.encryptionEnabled ? "Configured" : "Not configured"}
          />
        </div>
        <p className="text-xs text-neutral-500">
          Receiver address and encryption are managed on the server (SIA_RECEIVER_IP,
          SIA_RECEIVER_PORT, SIA_ENCRYPTION_KEY) and cannot be changed here — this
          prevents a hijacked admin session from diverting alarms. To change them,
          update the environment variables on the VPS and redeploy.
        </p>

        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <Field label="Account Number">
            <input type="text" value={form.accountNumber} onChange={(e) => update("accountNumber", e.target.value)} placeholder="e.g. A001" className={inputCls} />
          </Field>
          <Field label="Receiver Number">
            <input type="text" value={form.receiverNumber} onChange={(e) => update("receiverNumber", e.target.value)} placeholder="e.g. R0" className={inputCls} />
          </Field>
          <Field label="Account Prefix">
            <input type="text" value={form.accountPrefix} onChange={(e) => update("accountPrefix", e.target.value)} placeholder="e.g. L0" className={inputCls} />
          </Field>
          <Field label="Event Code">
            <input type="text" value={form.eventCode} onChange={(e) => update("eventCode", e.target.value)} placeholder="e.g. NPA" className={inputCls} />
          </Field>
        </div>

        <div className="flex flex-wrap items-center gap-3 pt-2">
          <button
            type="submit"
            disabled={saving}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {saving ? "Saving…" : "Save Settings"}
          </button>
          <button
            type="button"
            onClick={onTest}
            disabled={testing}
            className="rounded-md border border-black/15 px-4 py-2 text-sm font-medium hover:bg-black/5 disabled:opacity-50 dark:border-white/15 dark:hover:bg-white/5"
          >
            {testing ? "Testing…" : "Test Connection"}
          </button>
        </div>

        {saveStatus && <StatusMessage status={saveStatus} />}
        {testStatus && <StatusMessage status={testStatus} />}
      </form>
    </div>
  );
}

const inputCls =
  "w-full rounded-md border border-black/15 bg-transparent px-3 py-2 text-sm outline-none focus:border-blue-500 dark:border-white/15";

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1 block text-sm font-medium">{label}</label>
      {children}
      {hint && <p className="mt-1 text-xs text-neutral-500">{hint}</p>}
    </div>
  );
}

function ReadOnlyField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="mb-1 block text-sm font-medium">{label}</div>
      <div className="min-h-10 rounded-md border border-black/10 bg-black/[0.03] px-3 py-2 text-sm text-neutral-700 dark:border-white/10 dark:bg-white/[0.04] dark:text-neutral-200">
        {value}
      </div>
    </div>
  );
}

function StatusMessage({ status }: { status: NonNullable<Status> }) {
  const cls =
    status.kind === "success"
      ? "border-green-500/30 bg-green-500/10 text-green-700 dark:text-green-400"
      : "border-red-500/30 bg-red-500/10 text-red-700 dark:text-red-400";
  return <div className={`rounded-md border px-3 py-2 text-sm ${cls}`}>{status.text}</div>;
}
