import { Surreal } from 'surrealdb';
import { TaskConfig } from '@/types/taskConfig';

// Create a singleton instance of the SurrealDB client
const db = new Surreal();

// Constants for database connection
const DB_URL = 'http://localhost:8000';
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
const extractId = (id: RecordId<string> | string | undefined): string | undefined => {
  if (!id) return undefined;
  if (typeof id === 'string') return id;
  // RecordId to string conversion
  const idString = id.toString();
  return idString.split(':')[1] || idString;
};

/**
 * Map SurrealDB record to TaskConfig
 */
const mapToTaskConfig = (record: SurrealRecord, fallbackId?: string): TaskConfig => {
  return {
    id: extractId(record.id) || fallbackId,
    triggerType: record.triggerType as TaskConfig['triggerType'],
    directory: (record.directory as string) || '',
    taskType: (record.taskType as string) || '',
    sourceFile: record.sourceFile as string | undefined,
    destinationFile: record.destinationFile as string | undefined,
    time: record.time as string | undefined,
    interval: typeof record.interval === 'number' ? record.interval : undefined,
    printerName: record.printerName as string | undefined,
    archiveDirectory: record.archiveDirectory as string | undefined
  };
};

/**
 * SurrealDB service for handling database operations
 */
const surrealDbService = {
  /**
   * Initialize the database connection
   */
  init: async (): Promise<void> => {
    try {
      // Connect to the SurrealDB server
      await db.connect(DB_URL);
      
      // Sign in to the database
      await db.signin({
        username: DB_USERNAME,
        password: DB_PASSWORD,
      });
      
      // Select the namespace and database with correct parameter format
      await db.use({ namespace: DB_NAMESPACE, database: DB_DATABASE });
      
      console.log('Successfully connected to SurrealDB');
    } catch (error) {
      console.error('Failed to connect to SurrealDB:', error);
      throw error;
    }
  },

  /**
   * Get all task configs
   */
  getAllTaskConfigs: async (): Promise<TaskConfig[]> => {
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
  createTaskConfig: async (taskConfig: Omit<TaskConfig, 'id'>): Promise<TaskConfig | null> => {
    try {
      console.log('Creating task config:', taskConfig);
      const result = await db.create('taskconfig', taskConfig);
      
      if (!result || !Array.isArray(result) || result.length === 0) {
        console.error('Failed to create task: No result returned from database');
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
  updateTaskConfig: async (id: string, taskConfig: Partial<TaskConfig>): Promise<TaskConfig | null> => {
    try {
      console.log(`Updating task ${id} with:`, taskConfig);
      // Build SET clause from provided fields
      const setClause = Object.entries(taskConfig)
        .map(([key]) => `${key} = $${key}`)
        .join(', ');
      const sql = `UPDATE taskconfig:${id} SET ${setClause} RETURN AFTER;`;
      const queryResult = await db.query(sql, taskConfig as Record<string, unknown>);

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
