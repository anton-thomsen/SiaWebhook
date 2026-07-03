import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/api";

export async function POST() {
  const res = await apiFetch("/api/settings/test-connection", { method: "POST" });
  if (res.status === 401) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }
  const data = await res.json();
  return NextResponse.json(data, { status: res.status });
}
