import { Surreal } from 'surrealdb';
import { TaskConfig } from '@/types/taskConfig';
import { TaskLogging } from '@/types/taskLogging';

// Create a singleton instance of the SurrealDB client
const db = new Surreal();

// Constants for database connection
const DB_URL = 'ws://127.0.0.1:8000/rpc';
const DB_NAMESPACE = 'cronplus';
const DB_DATABASE = 'cronplus';
const DB_USERNAME = 'root';
const DB_PASSWORD = 'root';

// Define RecordId type from SurrealDB
type RecordId<T extends string = string> = {
  tb: string;
  id: T;
  toString(): string;
};

// Define type for SurrealDB record
interface SurrealRecord {
  id?: RecordId<string> | string;
  [key: string]: unknown;
}

/**
 * Convert RecordId to string
 */
const extractId = (
  id: RecordId<string> | string | undefined
): string | undefined => {
  if (!id) return undefined;
  if (typeof id === 'string') return id;
  // RecordId to string conversion
  const idString = id.toString();
  return idString.split(':')[1] || idString;
};

/**
 * Map SurrealDB record to TaskConfig
 */
const mapToTaskConfig = (
  record: SurrealRecord,
  fallbackId?: string
): TaskConfig => {
  return {
    id: extractId(record.id) || fallbackId,
    triggerType: record.triggerType as TaskConfig['triggerType'],
    sourceFolder: (record.sourceFolder as string) || '',
    destinationFolder: (record.destinationFolder as string) || '',
    taskType: (record.taskType as string) || '',
    sourceFile: record.sourceFile as string | undefined,
    destinationFile: record.destinationFile as string | undefined,
    time: record.time as string | undefined,
    interval: typeof record.interval === 'number' ? record.interval : undefined,
    printerName: record.printerName as string | undefined,
    archiveDirectory: record.archiveDirectory as string | undefined,
    createdAt: record.createdAt ? new Date(record.createdAt as string) : undefined,
    updatedAt: record.updatedAt ? new Date(record.updatedAt as string) : undefined
  };
};

/**
 * Map SurrealDB record to TaskLogging
 */
const mapToTaskLogging = (
  record: SurrealRecord,
  fallbackId?: string
): TaskLogging => {
  return {
    id: extractId(record.id) || fallbackId,
    taskType: record.TaskType as string,
    triggerType: record.TriggerType as TaskLogging['triggerType'],
    directory: (record.Directory as string) || '',
    filePath: record.FilePath as string | undefined,
    printerName: record.PrinterName as string | undefined,
    archiveDirectory: record.ArchiveDirectory as string | undefined,
    sourceFolder: (record.SourceFolder as string) || '',
    destinationFolder: (record.DestinationFolder as string) || '',
    triggeredAt: record.TriggeredAt ? new Date(record.TriggeredAt as string) : new Date(),
    result: (record.Result as string) || '',
    duration: typeof record.Duration === 'number' ? record.Duration : undefined,
    createdAt: record.CreatedAt ? new Date(record.CreatedAt as string) : undefined,
    updatedAt: record.UpdatedAt ? new Date(record.UpdatedAt as string) : undefined
  };
};

/**
 * Map TaskConfig to SurrealDB format
 */
const mapFromTaskConfig = (task: TaskConfig) => ({
  triggerType: task.triggerType,
  sourceFolder: task.sourceFolder,
  destinationFolder: task.destinationFolder,
  taskType: task.taskType,
  sourceFile: task.sourceFile,
  destinationFile: task.destinationFile,
  time: task.time,
  interval: task.interval,
  printerName: task.printerName,
  archiveDirectory: task.archiveDirectory,
  // Include timestamps when creating or updating records
  createdAt: task.createdAt || new Date().toISOString(),
  updatedAt: new Date().toISOString() // Always update the updatedAt timestamp
});

// Track initialization state
let isInitialized = false;

// Utility to ensure initialization
const ensureInitialized = async () => {
  if (!isInitialized) {
    await surrealDbService.init();
  }
};

/**
 * SurrealDB service for handling database operations
 */
const surrealDbService = {
  /**
   * Initialize the database connection
   */
  init: async (): Promise<void> => {
    if (isInitialized) return;
    try {
      // Connect to the SurrealDB server
      await db.connect(DB_URL);

      // Sign in with root credentials
      await db.signin({
        username: DB_USERNAME,
        password: DB_PASSWORD
      });

      // Select the namespace and database
      await db.use({ namespace: DB_NAMESPACE, database: DB_DATABASE });

      console.log('Successfully connected to SurrealDB');
      isInitialized = true;
    } catch (error) {
      console.error('Failed to connect to SurrealDB:', error);
      throw error;
    }
  },

  /**
   * Get all task configs
   */
  getAllTaskConfigs: async (): Promise<TaskConfig[]> => {
    await ensureInitialized();
    try {
      const result = await db.select('taskconfig');
      // Properly cast the result to the expected type
      return (Array.isArray(result) ? result : []).map((item: unknown) => {
        const record = item as SurrealRecord;
        return mapToTaskConfig(record);
      });
    } catch (error) {
      console.error('Failed to fetch task configs:', error);
      return [];
    }
  },

  /**
   * Get task config by ID
   */
  getTaskConfigById: async (id: string): Promise<TaskConfig | null> => {
    await ensureInitialized();
    try {
      const result = await db.select(`taskconfig:${id}`);
      if (!result || !Array.isArray(result) || result.length === 0) {
        return null;
      }

      const record = result[0] as unknown as SurrealRecord;
      return mapToTaskConfig(record, id);
    } catch (error) {
      console.error(`Failed to fetch task config with ID ${id}:`, error);
      return null;
    }
  },

  /**
   * Create a new task config
   */
  createTaskConfig: async (
    taskConfig: Omit<TaskConfig, 'id'>
  ): Promise<TaskConfig | null> => {
    await ensureInitialized();
    try {
      console.log('Creating task config:', taskConfig);
      const result = await db.create('taskconfig', mapFromTaskConfig(taskConfig));

      if (!result || !Array.isArray(result) || result.length === 0) {
        console.error(
          'Failed to create task: No result returned from database'
        );
        return null;
      }

      const record = result[0] as unknown as SurrealRecord;
      const newTask = mapToTaskConfig(record);
      console.log('Task created successfully:', newTask);
      return newTask;
    } catch (error) {
      console.error('Failed to create task config:', error);
      return null;
    }
  },

  /**
   * Update an existing task config
   */
  updateTaskConfig: async (
    id: string,
    taskConfig: Partial<TaskConfig>
  ): Promise<TaskConfig | null> => {
    await ensureInitialized();
    try {
      console.log(`Updating task ${id} with:`, taskConfig);
      // Build SET clause from provided fields
      const setClause = Object.entries(mapFromTaskConfig(taskConfig as TaskConfig))
        .map(([key]) => `${key} = $${key}`)
        .join(', ');
      const sql = `UPDATE taskconfig:${id} SET ${setClause} RETURN AFTER;`;
      const queryResult = await db.query(
        sql,
        mapFromTaskConfig(taskConfig as TaskConfig) as Record<string, unknown>
      );

      console.log('Update result:', queryResult);

      // Ensure valid result
      if (queryResult.length === 0) {
        console.error(`No result returned for update on ${id}`);
        return null;
      }
      const record = queryResult[0] as SurrealRecord;

      console.log('Updated record:', record);

      const updatedTask = mapToTaskConfig(record, id);
      console.log('Task updated successfully:', updatedTask);
      return updatedTask;
    } catch (error) {
      console.error(`Failed to update task config with ID ${id}:`, error);
      return null;
    }
  },

  /**
   * Delete a task config
   */
  deleteTaskConfig: async (id: string): Promise<boolean> => {
    await ensureInitialized();
    try {
      console.log(`Deleting task ${id}`);
      const sql = `DELETE taskconfig:${id} RETURN BEFORE;`;
      const queryResult = await db.query(sql);

      console.log('Delete result:', queryResult);

      // Ensure deletion happened
      if (queryResult.length === 0) {
        console.error(`No record deleted for ${id}`);
        return false;
      }
      return true;
    } catch (error) {
      console.error(`Failed to delete task config with ID ${id}:`, error);
      return false;
    }
  },

  /**
   * Get all task logs from the database
   */
  getTaskLogs: async (): Promise<TaskLogging[]> => {
    await ensureInitialized();
    try {
      console.log('Fetching task logs...');
      const result = await db.select('tasklogs');
      
      if (!result || !Array.isArray(result)) {
        console.error('Failed to fetch task logs: Invalid response format');
        return [];
      }
      
      const logs = result.map((record) => 
        mapToTaskLogging(record as unknown as SurrealRecord)
      );
      
      console.log(`Fetched ${logs.length} task logs`);
      return logs;
    } catch (error) {
      console.error('Failed to fetch task logs:', error);
      return [];
    }
  },
  
  /**
   * Get task logs with pagination
   */
  getTaskLogsPaginated: async (
    page: number = 1,
    limit: number = 10,
    sortField: string = 'triggeredAt',
    sortDirection: 'ASC' | 'DESC' = 'DESC'
  ): Promise<{ logs: TaskLogging[]; total: number }> => {
    await ensureInitialized();
    try {
      console.log(`Fetching task logs (page ${page}, limit ${limit})...`);
      
      // Calculate offset
      const offset = (page - 1) * limit;
      
      // Build the query with sorting and pagination
      const orderClause = `ORDER BY ${sortField} ${sortDirection}`;
      const limitClause = `LIMIT ${limit} START ${offset}`;
      
      const countResult = await db.query<[{ count: number }]>(`
        SELECT count() as count FROM tasklogs
      `);
      
      const result = await db.query<SurrealRecord[]>(`
        SELECT * FROM tasklogs ${orderClause} ${limitClause}
      `);
      
      if (!result || !Array.isArray(result) || result.length === 0 || !result[0]) {
        console.error('Failed to fetch paginated task logs: Invalid response format');
        return { logs: [], total: 0 };
      }
      
      // Extract logs from the result
      const resultArray = Array.isArray(result) && result.length > 0 ? result[0] : [];
      const logs = Array.isArray(resultArray) 
        ? resultArray.map((record) => mapToTaskLogging(record as SurrealRecord))
        : [];
      
      // Extract total count
      const total = countResult && Array.isArray(countResult) && 
                    countResult[0] && Array.isArray(countResult[0]) && 
                    countResult[0][0] && typeof countResult[0][0].count === 'number'
        ? countResult[0][0].count 
        : 0;
      
      console.log(`Fetched ${logs.length} task logs (total: ${total})`);
      return { logs, total };
    } catch (error) {
      console.error('Failed to fetch paginated task logs:', error);
      return { logs: [], total: 0 };
    }
  },

  /**
   * Close the database connection
   */
  close: async (): Promise<void> => {
    try {
      await db.close();
      console.log('Successfully closed SurrealDB connection');
    } catch (error) {
      console.error('Failed to close SurrealDB connection:', error);
    }
  }
};

export default surrealDbService;
