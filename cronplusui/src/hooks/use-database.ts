import { useContext } from 'react';
import { DatabaseContext } from '@/providers/database-provider';
import { TaskConfig } from '@/types/taskConfig';

interface DatabaseContextProps {
  isConnected: boolean;
  isLoading: boolean;
  error: string | null;
  taskConfigs: TaskConfig[];
  refreshTaskConfigs: () => Promise<void>;
  getTaskConfig: (id: string) => Promise<TaskConfig | null>;
  createTaskConfig: (
    taskConfig: Omit<TaskConfig, 'id'>
  ) => Promise<TaskConfig | null>;
  updateTaskConfig: (
    id: string,
    taskConfig: Partial<TaskConfig>
  ) => Promise<TaskConfig | null>;
  deleteTaskConfig: (id: string) => Promise<boolean>;
}

/**
 * Custom hook to use the database context
 */
export const useDatabase = (): DatabaseContextProps => {
  const context = useContext(DatabaseContext);

  if (context === undefined) {
    throw new Error('useDatabase must be used within a DatabaseProvider');
  }

  return context;
};
