export enum TriggerType {
  FileCreated = 'FileCreated',
  FileRenamed = 'FileRenamed',
  Time = 'Time',
  Interval = 'Interval'
}

export interface TaskConfig {
  id?: string;
  triggerType: TriggerType;
  directory: string;
  taskType: string;
  sourceFile?: string;
  destinationFile?: string;
  time?: string;
  interval?: number;
  printerName?: string;
  archiveDirectory?: string;
}
