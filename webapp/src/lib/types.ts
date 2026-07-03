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
