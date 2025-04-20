import { TriggerType } from './taskConfig';

export interface TaskLogging {
  id?: string;
  taskType: string;
  triggerType: TriggerType;
  directory: string;
  filePath?: string;
  printerName?: string;
  archiveDirectory?: string;
  sourceFolder: string;
  destinationFolder: string;
  triggeredAt: Date;
  result: string;
  duration?: number; // in milliseconds
  createdAt?: Date;
  updatedAt?: Date;
}
