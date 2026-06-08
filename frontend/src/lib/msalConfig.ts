"use client";

import { PublicClientApplication, type Configuration, type RedirectRequest } from "@azure/msal-browser";

// These are read at runtime from NEXT_PUBLIC_ env vars.
// Auth is optional for the demo — if vars are missing, MSAL is not enforced.
const tenantId = process.env.NEXT_PUBLIC_AAD_TENANT_ID ?? "common";
const clientId = process.env.NEXT_PUBLIC_AAD_CLIENT_ID ?? "00000000-0000-0000-0000-000000000000";
const redirectUri =
  typeof window !== "undefined"
    ? process.env.NEXT_PUBLIC_REDIRECT_URI
      ? new URL(process.env.NEXT_PUBLIC_REDIRECT_URI, window.location.origin).toString()
      : window.location.origin
    : "http://localhost:3000";

export const apiScope = process.env.NEXT_PUBLIC_AAD_API_SCOPE ?? "";

const msalConfig: Configuration = {
  auth: {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri,
    postLogoutRedirectUri: redirectUri,
  },
  cache: {
    cacheLocation: "sessionStorage",
  },
};

export const msalInstance = new PublicClientApplication(msalConfig);

export const loginRequest: RedirectRequest = {
  scopes: apiScope ? [apiScope, "openid", "profile", "email"] : ["openid", "profile", "email"],
};

export const apiTokenRequest = {
  scopes: apiScope ? [apiScope] : [],
};

/** True when MSAL is configured with a real client id. */
export function isMsalConfigured(): boolean {
  return clientId !== "00000000-0000-0000-0000-000000000000";
}
