import React, { createContext, useEffect, useState, ReactNode } from 'react';
import surrealDbService from '@/services/database/surrealDbService';
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

const DatabaseContext = createContext<DatabaseContextProps | undefined>(
  undefined
);

interface DatabaseProviderProps {
  children: ReactNode;
}

export const DatabaseProvider: React.FC<DatabaseProviderProps> = ({
  children
}) => {
  const [isConnected, setIsConnected] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [taskConfigs, setTaskConfigs] = useState<TaskConfig[]>([]);

  // Initialize database connection
  useEffect(() => {
    const connectToDatabase = async () => {
      try {
        setIsLoading(true);
        setError(null);
        await surrealDbService.init();
        setIsConnected(true);
        await refreshTaskConfigs();
      } catch (err) {
        setError(
          `Failed to connect to database: ${err instanceof Error ? err.message : String(err)}`
        );
        console.error('Database connection error:', err);
      } finally {
        setIsLoading(false);
      }
    };

    connectToDatabase();

    // Clean up database connection when component unmounts
    return () => {
      const disconnectFromDatabase = async () => {
        if (isConnected) {
          try {
            await surrealDbService.close();
          } catch (err) {
            console.error('Error closing database connection:', err);
          }
        }
      };

      disconnectFromDatabase();
    };
  }, [isConnected]);

  // Fetch all task configs from the database
  const refreshTaskConfigs = async (): Promise<void> => {
    try {
      setIsLoading(true);
      const configs = await surrealDbService.getAllTaskConfigs();

      console.log('Fetched task configs:', configs);

      setTaskConfigs(configs);
    } catch (err) {
      setError(
        `Failed to fetch task configs: ${err instanceof Error ? err.message : String(err)}`
      );
      console.error('Error fetching task configs:', err);
    } finally {
      setIsLoading(false);
    }
  };

  // Get a specific task config by ID
  const getTaskConfig = async (id: string): Promise<TaskConfig | null> => {
    return await surrealDbService.getTaskConfigById(id);
  };

  // Create a new task config
  const createTaskConfig = async (
    taskConfig: Omit<TaskConfig, 'id'>
  ): Promise<TaskConfig | null> => {
    const newConfig = await surrealDbService.createTaskConfig(taskConfig);
    if (newConfig) {
      await refreshTaskConfigs();
    }
    return newConfig;
  };

  // Update an existing task config
  const updateTaskConfig = async (
    id: string,
    taskConfig: Partial<TaskConfig>
  ): Promise<TaskConfig | null> => {
    const updatedConfig = await surrealDbService.updateTaskConfig(
      id,
      taskConfig
    );
    if (updatedConfig) {
      await refreshTaskConfigs();
    }
    return updatedConfig;
  };

  // Delete a task config
  const deleteTaskConfig = async (id: string): Promise<boolean> => {
    const success = await surrealDbService.deleteTaskConfig(id);
    if (success) {
      await refreshTaskConfigs();
    }
    return success;
  };

  const value = {
    isConnected,
    isLoading,
    error,
    taskConfigs,
    refreshTaskConfigs,
    getTaskConfig,
    createTaskConfig,
    updateTaskConfig,
    deleteTaskConfig
  };

  return (
    <DatabaseContext.Provider value={value}>
      {children}
    </DatabaseContext.Provider>
  );
};

// Export the context for use in the hook
export { DatabaseContext };
