import { NextRequest, NextResponse } from "next/server";
import { SESSION_COOKIE } from "@/lib/constants";
import { API_BASE_URL } from "@/lib/api";

/**
 * Exchanges the admin password for a JWT via the .NET API, then stores that JWT
 * in an httpOnly cookie. The token never reaches the browser's JavaScript.
 */
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => null);
  const password = body?.password;
  if (typeof password !== "string" || password.length === 0) {
    return NextResponse.json({ error: "Password required" }, { status: 400 });
  }

  const res = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ password }),
    cache: "no-store",
  });

  if (res.status === 429) {
    return NextResponse.json({ error: "Too many attempts. Try again shortly." }, { status: 429 });
  }
  if (!res.ok) {
    return NextResponse.json({ error: "Incorrect password." }, { status: 401 });
  }

  const { token, expiresUtc } = (await res.json()) as { token: string; expiresUtc: string };

  const response = NextResponse.json({ ok: true });
  response.cookies.set({
    name: SESSION_COOKIE,
    value: token,
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    expires: new Date(expiresUtc),
  });
  return response;
}
