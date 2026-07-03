import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/api";

export async function GET() {
  const res = await apiFetch("/api/settings");
  if (res.status === 401) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }
  const data = await res.json();
  return NextResponse.json(data, { status: res.status });
}

export async function PUT(req: NextRequest) {
  const body = await req.text();
  const res = await apiFetch("/api/settings", { method: "PUT", body });

  if (res.status === 401) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }
  if (!res.ok) {
    const text = await res.text();
    return NextResponse.json({ error: text || "Save failed" }, { status: res.status });
  }
  return NextResponse.json({ ok: true });
}
