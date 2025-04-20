import { useState, useEffect, useCallback } from 'react';
import { TaskConfig } from '@/types/taskConfig';
import { TaskLogging } from '@/types/taskLogging';
import surrealDbService from '@/services/database/surrealDbService';

interface DatabaseContextProps {
  isConnected: boolean;
  isLoading: boolean;
  error: string | null;
  taskConfigs: TaskConfig[];
  taskLogs: TaskLogging[];
  isLogLoading: boolean;
  logError: string | null;
  refreshTaskConfigs: () => Promise<void>;
  refreshTaskLogs: () => Promise<void>;
  getTaskLogsPaginated: (
    page: number,
    limit: number,
    sortField: string,
    sortDirection: 'ASC' | 'DESC'
  ) => Promise<{ logs: TaskLogging[]; total: number }>;
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
  const [taskConfigs, setTaskConfigs] = useState<TaskConfig[]>([]);
  const [taskLogs, setTaskLogs] = useState<TaskLogging[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isLogLoading, setIsLogLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [logError, setLogError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);

  // Initialize database
  const initializeDatabase = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      await surrealDbService.init();
      setIsConnected(true);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Unknown error';
      setError(`Failed to initialize database: ${errorMessage}`);
      console.error('Error initializing database:', err);
      setIsConnected(false);
    } finally {
      setIsLoading(false);
    }
  }, []);

  // Refresh task configs
  const refreshTaskConfigs = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      const configs = await surrealDbService.getAllTaskConfigs();
      setTaskConfigs(configs);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Unknown error';
      setError(`Failed to fetch task configs: ${errorMessage}`);
      console.error('Error fetching task configs:', err);
    } finally {
      setIsLoading(false);
    }
  }, []);

  // Refresh task logs
  const refreshTaskLogs = useCallback(async () => {
    setIsLogLoading(true);
    setLogError(null);

    try {
      const logs = await surrealDbService.getTaskLogs();
      setTaskLogs(logs);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Unknown error';
      setLogError(`Failed to fetch task logs: ${errorMessage}`);
      console.error('Error fetching task logs:', err);
    } finally {
      setIsLogLoading(false);
    }
  }, []);

  // Load paginated task logs
  const getTaskLogsPaginated = useCallback(
    async (
      page: number = 1,
      limit: number = 10,
      sortField: string = 'triggeredAt',
      sortDirection: 'ASC' | 'DESC' = 'DESC'
    ) => {
      setIsLogLoading(true);
      setLogError(null);

      try {
        return await surrealDbService.getTaskLogsPaginated(
          page,
          limit,
          sortField,
          sortDirection
        );
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Unknown error';
        setLogError(`Failed to fetch task logs: ${errorMessage}`);
        console.error('Error fetching paginated task logs:', err);
        return { logs: [], total: 0 };
      } finally {
        setIsLogLoading(false);
      }
    },
    []
  );

  // Initialize database on component mount
  useEffect(() => {
    initializeDatabase();
  }, [initializeDatabase]);

  // Load task configs after database is initialized
  useEffect(() => {
    if (isConnected) {
      refreshTaskConfigs();
    }
  }, [isConnected, refreshTaskConfigs]);

  // Load task logs after database is initialized
  useEffect(() => {
    if (isConnected) {
      refreshTaskLogs();
    }
  }, [isConnected, refreshTaskLogs]);

  return {
    isConnected,
    isLoading,
    error,
    taskConfigs,
    taskLogs,
    isLogLoading,
    logError,
    refreshTaskConfigs,
    refreshTaskLogs,
    getTaskLogsPaginated,
    getTaskConfig: surrealDbService.getTaskConfigById,
    createTaskConfig: surrealDbService.createTaskConfig,
    updateTaskConfig: surrealDbService.updateTaskConfig,
    deleteTaskConfig: surrealDbService.deleteTaskConfig,
  };
};
