// Mirrors SiaWebhookApi.Configuration.SiaSettings (camelCase from System.Text.Json).
export interface SiaSettings {
  ipAddress: string;
  port: number;
  encryptionKey: string | null;
  accountNumber: string;
  receiverNumber: string;
  accountPrefix: string;
  eventCode: string;
}
