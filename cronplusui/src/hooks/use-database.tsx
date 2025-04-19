import { useContext } from 'react';
import { DatabaseContext } from '@/providers/database-provider';

export function useDatabase() {
  const context = useContext(DatabaseContext);

  if (!context) {
    throw new Error('useDatabase must be used within a DatabaseProvider');
  }

  return context;
}
