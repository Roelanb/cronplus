export enum TriggerType {
  FileCreated = 'FileCreated',
  FileRenamed = 'FileRenamed',
  Time = 'Time',
  Interval = 'Interval'
}

export interface TaskConfig {
  id?: string;
  triggerType: TriggerType;
  sourceFolder: string;
  destinationFolder: string;
  taskType: string;
  sourceFile?: string;
  destinationFile?: string;
  time?: string;
  interval?: number;
  printerName?: string;
  archiveDirectory?: string;
  createdAt?: Date;
  updatedAt?: Date;
}
