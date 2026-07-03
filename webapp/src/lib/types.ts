// Mirrors SiaWebhookApi.Configuration.SiaSettings (camelCase from System.Text.Json).
export interface SiaSettings {
  ipAddress: string;
  port: number;
  encryptionEnabled: boolean;
  accountNumber: string;
  receiverNumber: string;
  accountPrefix: string;
  eventCode: string;
}

export type SiaSettingsUpdate = Pick<
  SiaSettings,
  "accountNumber" | "receiverNumber" | "accountPrefix" | "eventCode"
>;
