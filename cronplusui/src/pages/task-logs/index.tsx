import { useState, useEffect, useCallback, ReactNode, useRef } from 'react';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle
} from '@/components/ui/card';
import PageHead from '@/components/shared/page-head';
import { useDatabase } from '@/hooks/use-database';
import { TaskLogging } from '@/types/taskLogging';
import { Badge } from '@/components/ui/badge';
import { TriggerType } from '@/types/taskConfig';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow
} from '@/components/ui/table';
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious
} from '@/components/ui/pagination';
import { RefreshCcwIcon, CheckCircleIcon, XCircleIcon, EyeOffIcon, EyeIcon } from 'lucide-react';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue
} from '@/components/ui/select';

// --- COLUMN CONFIG ---
const ALL_COLUMNS = [
  { key: 'taskType', label: 'Task Type', always: false },
  { key: 'triggerType', label: 'Trigger Type', always: false },
  { key: 'sourceFolder', label: 'Source Folder', always: false },
  { key: 'filePath', label: 'File Path', always: false },
  { key: 'triggeredAt', label: 'Triggered At', always: false },
  { key: 'duration', label: 'Duration', always: false },
  { key: 'result', label: 'Result', always: false }
];

const getDefaultVisibleColumns = () => ALL_COLUMNS.map(col => col.key);

export default function TaskLogsPage() {
  // State for pagination
  const [page, setPage] = useState(1);
  const [limit, setLimit] = useState(10);
  const [logs, setLogs] = useState<TaskLogging[]>([]);
  const [totalLogs, setTotalLogs] = useState(0);
  const [sortField, setSortField] = useState('triggeredAt');
  const [sortDirection, setSortDirection] = useState<'ASC' | 'DESC'>('DESC');
  // --- Visible Columns State ---
  const [visibleColumns, setVisibleColumns] = useState<string[]>(getDefaultVisibleColumns());
  const [showColumnMenu, setShowColumnMenu] = useState(false);

  const menuRef = useRef<HTMLDivElement>(null);

  // --- Click-away handler for column menu ---
  useEffect(() => {
    if (!showColumnMenu) return;
    const handleClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setShowColumnMenu(false);
      }
    };
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [showColumnMenu]);

  const { isLogLoading, logError, getTaskLogsPaginated, refreshTaskLogs } = useDatabase();

  // --- Persist visible columns in localStorage ---
  useEffect(() => {
    const stored = localStorage.getItem('taskLogsVisibleColumns');
    if (stored) {
      setVisibleColumns(JSON.parse(stored));
    }
  }, []);
  useEffect(() => {
    localStorage.setItem('taskLogsVisibleColumns', JSON.stringify(visibleColumns));
  }, [visibleColumns]);

  // --- Column Toggle Handlers ---
  const handleToggleColumn = (key: string) => {
    setVisibleColumns((prev) =>
      prev.includes(key)
        ? prev.filter((col) => col !== key)
        : [...prev, key]
    );
  };

  // Helper function to get trigger type label
  const getTriggerTypeLabel = (triggerType: TriggerType) => {
    const labels: Record<TriggerType, string> = {
      [TriggerType.FileCreated]: 'File Created',
      [TriggerType.FileRenamed]: 'File Renamed',
      [TriggerType.Time]: 'Schedule (Cron)',
      [TriggerType.Interval]: 'Interval'
    };

    return labels[triggerType] || 'Unknown';
  };

  // Helper function to get the task type badge color
  const getTaskTypeBadge = (taskType: string) => {
    const taskTypeColors: Record<string, string> = {
      Copy: 'bg-blue-500 hover:bg-blue-600',
      Move: 'bg-green-500 hover:bg-green-600',
      Delete: 'bg-red-500 hover:bg-red-600',
      Print: 'bg-purple-500 hover:bg-purple-600'
    };

    return taskTypeColors[taskType] || 'bg-gray-500 hover:bg-gray-600';
  };

  // Helper function to format the date
  const formatDate = (date: Date | string) => {
    const d = new Date(date);
    return d.toLocaleString();
  };

  // Helper function to format the duration
  const formatDuration = (duration?: number) => {
    if (!duration) return 'N/A';
    
    if (duration < 1000) {
      return `${duration}ms`;
    }
    
    const seconds = Math.floor(duration / 1000);
    const ms = duration % 1000;
    
    return `${seconds}s ${ms}ms`;
  };

  // Load logs with pagination
  const loadLogs = useCallback(async () => {
    const response = await getTaskLogsPaginated(
      page,
      limit,
      sortField,
      sortDirection
    );
    setLogs(response.logs);
    setTotalLogs(response.total);
  }, [page, limit, sortField, sortDirection, getTaskLogsPaginated]);

  // Handle pagination change
  const handlePageChange = (newPage: number) => {
    setPage(newPage);
  };

  // Handle sorting change
  const handleSortChange = (field: string) => {
    if (field === sortField) {
      // Toggle direction if clicking the same field
      setSortDirection(sortDirection === 'ASC' ? 'DESC' : 'ASC');
    } else {
      // Default to DESC for new sort field
      setSortField(field);
      setSortDirection('DESC');
    }
  };

  // Load logs when pagination or sorting changes
  useEffect(() => {
    loadLogs();
  }, [loadLogs]);

  // Calculate pagination information
  const totalPages = Math.ceil(totalLogs / limit);
  const startItem = (page - 1) * limit + 1;
  const endItem = Math.min(page * limit, totalLogs);

  // Generate pagination items
  const getPaginationItems = () => {
    const items: ReactNode[] = [];
    const maxVisiblePages = 5; // Show up to 5 page numbers

    // Always show first page
    items.push(
      <PaginationItem key="first">
        <PaginationLink
          onClick={() => handlePageChange(1)}
          isActive={page === 1}
        >
          1
        </PaginationLink>
      </PaginationItem>
    );

    // Calculate range of visible pages
    let startPage = Math.max(2, page - Math.floor(maxVisiblePages / 2));
    const endPage = Math.min(totalPages - 1, startPage + maxVisiblePages - 2);

    // Adjust start if we're near the end
    if (endPage - startPage < maxVisiblePages - 2) {
      startPage = Math.max(2, endPage - (maxVisiblePages - 2));
    }

    // Add ellipsis after first page if needed
    if (startPage > 2) {
      items.push(
        <PaginationItem key="ellipsis-start">
          <PaginationEllipsis />
        </PaginationItem>
      );
    }

    // Add page numbers
    for (let i = startPage; i <= endPage; i++) {
      items.push(
        <PaginationItem key={`page-${i}`}>
          <PaginationLink
            onClick={() => handlePageChange(i)}
            isActive={page === i}
          >
            {i}
          </PaginationLink>
        </PaginationItem>
      );
    }

    // Add ellipsis before last page if needed
    if (endPage < totalPages - 1) {
      items.push(
        <PaginationItem key="ellipsis-end">
          <PaginationEllipsis />
        </PaginationItem>
      );
    }

    // Always show last page if there are more than 1 page
    if (totalPages > 1) {
      items.push(
        <PaginationItem key="last">
          <PaginationLink
            onClick={() => handlePageChange(totalPages)}
            isActive={page === totalPages}
          >
            {totalPages}
          </PaginationLink>
        </PaginationItem>
      );
    }

    return items;
  };

  return (
    <>
      <PageHead title="Task Logs | CronPlus" />
      <div className="max-h-screen flex-1 space-y-4 overflow-y-auto p-4 pt-6 md:p-8">
        <div className="flex items-center justify-between space-y-2">
          <h2 className="text-3xl font-bold tracking-tight">Task Logs</h2>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              aria-label="Refresh logs"
              onClick={refreshTaskLogs}
              className="flex items-center gap-1"
            >
              <RefreshCcwIcon className="size-4" /> Refresh
            </Button>
            {/* COLUMN TOGGLE BUTTON */}
            <div className="relative">
              <Button
                variant="outline"
                size="sm"
                aria-label="Show/hide columns"
                className="flex items-center gap-1"
                onClick={() => setShowColumnMenu((v) => !v)}
                tabIndex={0}
              >
                <EyeIcon className="size-4" /> Columns
              </Button>
              {showColumnMenu && (
                <div
                  ref={menuRef}
                  className="absolute left-0 mt-2 z-10 w-48 bg-gray-800 text-gray-100 font-medium border border-gray-700 rounded shadow-lg p-2"
                  tabIndex={0}
                >
                  <div className="flex flex-col gap-1">
                    {ALL_COLUMNS.map((col) => (
                      <label
                        key={col.key}
                        className="flex items-center gap-2 cursor-pointer px-2 py-1 rounded hover:bg-gray-700"
                      >
                        <input
                          type="checkbox"
                          checked={visibleColumns.includes(col.key)}
                          onChange={() => handleToggleColumn(col.key)}
                          className="accent-blue-400"
                          aria-label={`Toggle ${col.label} column`}
                        />
                        <span className="whitespace-nowrap">{col.label}</span>
                      </label>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>

        {logError && (
          <Card className="border-red-500 bg-red-50 dark:bg-red-950/30">
            <CardHeader className="pb-2">
              <CardTitle className="text-red-600 dark:text-red-400">
                Error
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p>{logError}</p>
              <Button
                variant="outline"
                className="mt-2"
                onClick={refreshTaskLogs}
              >
                Retry
              </Button>
            </CardContent>
          </Card>
        )}

        <Card>
          <CardHeader className="pb-2">
            <div className="flex items-center justify-between">
              <div>
                <CardTitle>Task Execution Logs</CardTitle>
                <CardDescription>
                  {isLogLoading
                    ? 'Loading...'
                    : totalLogs === 0
                    ? 'No logs found'
                    : `Showing ${startItem}-${endItem} of ${totalLogs} logs`}
                </CardDescription>
              </div>
              <div className="flex items-center gap-2">
                <div className="flex items-center gap-2">
                  <p className="text-sm text-muted-foreground">Rows</p>
                  <Select
                    value={limit.toString()}
                    onValueChange={(value) => setLimit(parseInt(value))}
                  >
                    <SelectTrigger className="h-8 w-16">
                      <SelectValue placeholder={limit} />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="5">5</SelectItem>
                      <SelectItem value="10">10</SelectItem>
                      <SelectItem value="20">20</SelectItem>
                      <SelectItem value="50">50</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>
            </div>
          </CardHeader>
          <CardContent>
            {isLogLoading ? (
              <div className="flex items-center justify-center p-8">
                <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary border-t-transparent"></div>
              </div>
            ) : logs.length > 0 ? (
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      {ALL_COLUMNS.filter(col => visibleColumns.includes(col.key)).map((col) => (
                        <TableHead key={col.key} className="whitespace-nowrap">
                          <Button
                            variant="ghost"
                            className="h-full w-full justify-start p-0 font-semibold"
                            onClick={() => handleSortChange(col.key)}
                          >
                            {col.label}
                            {sortField === col.key && (
                              <span className="ml-1">{sortDirection === 'ASC' ? '↑' : '↓'}</span>
                            )}
                          </Button>
                        </TableHead>
                      ))}
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {logs.map((log) => (
                      <TableRow key={log.id}>
                        {ALL_COLUMNS.filter(col => visibleColumns.includes(col.key)).map((col) => {
                          switch (col.key) {
                            case 'taskType':
                              return (
                                <TableCell key={col.key}>
                                  <Badge className={getTaskTypeBadge(log.taskType)}>
                                    {log.taskType}
                                  </Badge>
                                </TableCell>
                              );
                            case 'triggerType':
                              return (
                                <TableCell key={col.key}>
                                  <Badge variant="outline">
                                    {getTriggerTypeLabel(log.triggerType)}
                                  </Badge>
                                </TableCell>
                              );
                            case 'sourceFolder':
                              return (
                                <TableCell key={col.key} className="max-w-xs truncate" title={log.sourceFolder}>
                                  {log.sourceFolder}
                                </TableCell>
                              );
                            case 'filePath':
                              return (
                                <TableCell key={col.key} className="max-w-xs truncate" title={log.filePath || ''}>
                                  {log.filePath || '-'}
                                </TableCell>
                              );
                            case 'triggeredAt':
                              return (
                                <TableCell key={col.key}>{formatDate(log.triggeredAt)}</TableCell>
                              );
                            case 'duration':
                              return (
                                <TableCell key={col.key}>{formatDuration(log.duration)}</TableCell>
                              );
                            case 'result':
                              return (
                                <TableCell key={col.key}>
                                  <div className="flex items-center gap-1">
                                    {log.result.toLowerCase().includes('success') ? (
                                      <>
                                        <CheckCircleIcon className="size-4 text-green-500" />
                                        <span className="text-green-600">Success</span>
                                      </>
                                    ) : (
                                      <>
                                        <XCircleIcon className="size-4 text-red-500" />
                                        <span 
                                          className="text-red-600 max-w-32 truncate" 
                                          title={log.result}
                                        >
                                          {log.result || 'Failed'}
                                        </span>
                                      </>
                                    )}
                                  </div>
                                </TableCell>
                              );
                            default:
                              return null;
                          }
                        })}
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            ) : (
              <div className="flex items-center justify-center p-8">
                <p className="text-muted-foreground">No logs found</p>
              </div>
            )}

            {/* Pagination */}
            {logs.length > 0 && (
              <div className="mt-4">
                <Pagination>
                  <PaginationContent>
                    <PaginationItem>
                      <PaginationPrevious
                        onClick={() => handlePageChange(Math.max(1, page - 1))}
                        className={page === 1 ? 'pointer-events-none opacity-50' : ''}
                      />
                    </PaginationItem>

                    {getPaginationItems()}

                    <PaginationItem>
                      <PaginationNext
                        onClick={() => handlePageChange(Math.min(totalPages, page + 1))}
                        className={page === totalPages ? 'pointer-events-none opacity-50' : ''}
                      />
                    </PaginationItem>
                  </PaginationContent>
                </Pagination>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </>
  );
}
