import { NextResponse } from "next/server";
import { SESSION_COOKIE } from "@/lib/constants";

/** Clears the session cookie. The JWT itself is stateless and simply expires. */
export async function POST() {
  const response = NextResponse.json({ ok: true });
  response.cookies.set({ name: SESSION_COOKIE, value: "", httpOnly: true, path: "/", maxAge: 0 });
  return response;
}
