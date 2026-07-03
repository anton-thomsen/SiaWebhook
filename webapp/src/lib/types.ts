// Mirrors SiaWebhookApi.Configuration.SiaSettingsResponse (camelCase from System.Text.Json).
// The encryption key itself never reaches the browser — only whether one is configured.
export interface SiaSettings {
  ipAddress: string;
  port: number;
  encryptionEnabled: boolean;
  accountNumber: string;
  receiverNumber: string;
  accountPrefix: string;
  eventCode: string;
}

// The only fields the UI may change. Receiver IP/port and the encryption key
// are env-managed on the server and read-only here.
export type SiaSettingsUpdate = Pick<
  SiaSettings,
  "accountNumber" | "receiverNumber" | "accountPrefix" | "eventCode"
>;
