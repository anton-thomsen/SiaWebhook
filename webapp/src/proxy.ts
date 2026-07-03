import { NextRequest, NextResponse } from "next/server";
import { SESSION_COOKIE } from "@/lib/constants";

/**
 * First-gate route protection by cookie presence (Next.js 16 `proxy` convention,
 * formerly `middleware`). This only decides which shell to render — real
 * authorization is enforced by the .NET API, which validates the JWT signature
 * and expiry on every /api/settings call. An expired-but-present cookie still
 * gets bounced to /login by the page's own 401 handling.
 */
export function proxy(req: NextRequest) {
  const hasSession = req.cookies.has(SESSION_COOKIE);
  const isLogin = req.nextUrl.pathname === "/login";

  if (!hasSession && !isLogin) {
    return NextResponse.redirect(new URL("/login", req.url));
  }
  if (hasSession && isLogin) {
    return NextResponse.redirect(new URL("/", req.url));
  }
  return NextResponse.next();
}

// Run on everything except API routes and Next internals/static assets.
export const config = {
  matcher: ["/((?!api|_next/static|_next/image|favicon.ico).*)"],
};
