import { getToken } from "./auth";

// Server-side only. In Docker this points at the internal .NET service (e.g. http://api:8080);
// in local dev it defaults to the Kestrel dev port. Never exposed to the browser.
const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:5257";

/**
 * Calls the .NET API, attaching the admin JWT from the session cookie as a Bearer
 * token. This is the BFF boundary: the browser never sees the token or the API URL.
 */
export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const token = await getToken();
  const headers = new Headers(init?.headers);
  if (token) headers.set("Authorization", `Bearer ${token}`);
  if (!headers.has("Content-Type")) headers.set("Content-Type", "application/json");

  return fetch(`${API_BASE_URL}${path}`, { ...init, headers, cache: "no-store" });
}

export { API_BASE_URL };
