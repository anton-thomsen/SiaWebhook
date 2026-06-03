import { cookies } from "next/headers";
import { SESSION_COOKIE } from "./constants";

/** Reads the admin JWT from the httpOnly session cookie (server-side only). */
export async function getToken(): Promise<string | undefined> {
  const store = await cookies();
  return store.get(SESSION_COOKIE)?.value;
}
