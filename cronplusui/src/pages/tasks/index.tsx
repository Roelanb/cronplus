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
import { TriggerType, TaskConfig } from '@/types/taskConfig';
import { PlusIcon, FileEditIcon, TrashIcon, GridIcon, ListIcon } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { TaskForm } from '@/components/forms/task-form';
import { DeleteTaskDialog } from '@/components/modals/delete-task-dialog';
import { useState } from 'react';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow
} from '@/components/ui/table';
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";

export default function TasksPage() {
  const { taskConfigs, isLoading, error, refreshTaskConfigs } = useDatabase();

  // State for managing task forms and delete dialogs
  const [isCreateDialogOpen, setIsCreateDialogOpen] = useState(false);
  const [isEditDialogOpen, setIsEditDialogOpen] = useState(false);
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);
  const [selectedTask, setSelectedTask] = useState<TaskConfig | undefined>(
    undefined
  );
  // Add state for view mode
  const [viewMode, setViewMode] = useState<'grid' | 'list'>('grid');

  const handleCreateTask = () => {
    setIsCreateDialogOpen(true);
  };

  const handleEditTask = (task: TaskConfig) => {
    setSelectedTask(task);
    setIsEditDialogOpen(true);
  };

  const handleDeleteTask = (task: TaskConfig) => {
    setSelectedTask(task);
    setIsDeleteDialogOpen(true);
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

  // Helper function to render timing information based on trigger type
  const renderTimingInfo = (task: TaskConfig) => {
    switch (task.triggerType) {
      case TriggerType.Time:
        return (
          <p>
            <span className="font-medium">Schedule:</span>{' '}
            {task.time || 'Not specified'}
          </p>
        );
      case TriggerType.Interval:
        return (
          <p>
            <span className="font-medium">Interval:</span>{' '}
            {task.interval ? `${task.interval} seconds` : 'Not specified'}
          </p>
        );
      default:
        return null;
    }
  };

  return (
    <>
      <PageHead title="Tasks | CronPlus" />
      <div className="max-h-screen flex-1 space-y-4 overflow-y-auto p-4 pt-6 md:p-8">
        <div className="flex items-center justify-between space-y-2">
          <h2 className="text-3xl font-bold tracking-tight">Tasks</h2>
          <div className="flex items-center gap-4">
            <ToggleGroup type="single" value={viewMode} onValueChange={(value) => value && setViewMode(value as 'grid' | 'list')}>
              <ToggleGroupItem value="grid" aria-label="Grid view">
                <GridIcon className="size-4" />
              </ToggleGroupItem>
              <ToggleGroupItem value="list" aria-label="List view">
                <ListIcon className="size-4" />
              </ToggleGroupItem>
            </ToggleGroup>
            <Button
              onClick={handleCreateTask}
              className="flex items-center gap-2"
            >
              <PlusIcon className="size-4" />
              Create Task
            </Button>
          </div>
        </div>

        {error && (
          <Card className="border-red-500 bg-red-50 dark:bg-red-950/30">
            <CardHeader className="pb-2">
              <CardTitle className="text-red-600 dark:text-red-400">
                Error
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p>{error}</p>
              <Button
                variant="outline"
                className="mt-2"
                onClick={() => refreshTaskConfigs()}
              >
                Retry
              </Button>
            </CardContent>
          </Card>
        )}

        {isLoading ? (
          <div className="flex items-center justify-center p-8">
            <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary border-t-transparent"></div>
          </div>
        ) : taskConfigs && taskConfigs.length > 0 ? (
          viewMode === 'grid' ? (
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              {taskConfigs.map((task) => (
                <Card key={task.id} className="transition-shadow hover:shadow-md">
                  <CardHeader className="pb-2">
                    <div className="flex items-center justify-between">
                      <Badge className={getTaskTypeBadge(task.taskType)}>
                        {task.taskType}
                      </Badge>
                      <Badge variant="outline">
                        {getTriggerTypeLabel(task.triggerType)}
                      </Badge>
                    </div>
                    <CardDescription className="mt-2">
                      ID: {task.id || 'New Task'}
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="mb-4 space-y-2">
                      <p>
                        <span className="font-medium">Source Folder:</span>{' '}
                        {task.sourceFolder}
                      </p>
                      <p>
                        <span className="font-medium">Destination Folder:</span>{' '}
                        {task.destinationFolder}
                      </p>
                      {renderTimingInfo(task)}
                      {task.sourceFile && (
                        <p>
                          <span className="font-medium">Source:</span>{' '}
                          {task.sourceFile}
                        </p>
                      )}
                      {task.destinationFile && (
                        <p>
                          <span className="font-medium">Destination:</span>{' '}
                          {task.destinationFile}
                        </p>
                      )}
                      {task.printerName && (
                        <p>
                          <span className="font-medium">Printer:</span>{' '}
                          {task.printerName}
                        </p>
                      )}
                      {task.archiveDirectory && (
                        <p>
                          <span className="font-medium">Archive:</span>{' '}
                          {task.archiveDirectory}
                        </p>
                      )}
                    </div>
                    <div className="flex gap-2">
                      <Button
                        size="sm"
                        variant="outline"
                        className="flex items-center gap-1"
                        onClick={() => handleEditTask(task)}
                      >
                        <FileEditIcon className="size-4" />
                        Edit
                      </Button>
                      <Button
                        size="sm"
                        variant="outline"
                        className="flex items-center gap-1 text-red-600 hover:bg-red-50 hover:text-red-700"
                        onClick={() => handleDeleteTask(task)}
                      >
                        <TrashIcon className="size-4" />
                        Delete
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
          ) : (
            <Card>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Type</TableHead>
                    <TableHead>Trigger</TableHead>
                    <TableHead>Source Folder</TableHead>
                    <TableHead>Destination Folder</TableHead>
                    <TableHead>Created</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {taskConfigs.map((task) => (
                    <TableRow key={task.id}>
                      <TableCell>
                        <Badge className={getTaskTypeBadge(task.taskType)}>
                          {task.taskType}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <Badge variant="outline">
                          {getTriggerTypeLabel(task.triggerType)}
                        </Badge>
                      </TableCell>
                      <TableCell className="max-w-xs truncate" title={task.sourceFolder}>
                        {task.sourceFolder}
                      </TableCell>
                      <TableCell className="max-w-xs truncate" title={task.destinationFolder}>
                        {task.destinationFolder}
                      </TableCell>
                      <TableCell>
                        {task.createdAt ? new Date(task.createdAt).toLocaleDateString() : 'N/A'}
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex items-center justify-end gap-2">
                          <Button
                            size="sm"
                            variant="ghost"
                            className="flex h-8 w-8 p-0"
                            onClick={() => handleEditTask(task)}
                            aria-label="Edit task"
                          >
                            <FileEditIcon className="size-4" />
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            className="flex h-8 w-8 p-0 text-red-600 hover:bg-red-50 hover:text-red-700"
                            onClick={() => handleDeleteTask(task)}
                            aria-label="Delete task"
                          >
                            <TrashIcon className="size-4" />
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Card>
          )
        ) : (
          <Card>
            <CardHeader className="pb-2">
              <CardTitle>No tasks found</CardTitle>
              <CardDescription>
                Create your first task to get started
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Button
                variant="outline"
                className="flex items-center gap-2"
                onClick={handleCreateTask}
              >
                <PlusIcon className="size-4" />
                Create Task
              </Button>
            </CardContent>
          </Card>
        )}
      </div>

      {/* Create Task Dialog */}
      <TaskForm
        open={isCreateDialogOpen}
        onOpenChange={setIsCreateDialogOpen}
        mode="create"
      />

      {/* Edit Task Dialog */}
      {selectedTask && (
        <TaskForm
          open={isEditDialogOpen}
          onOpenChange={setIsEditDialogOpen}
          initialData={selectedTask}
          mode="edit"
        />
      )}

      {/* Delete Task Dialog */}
      <DeleteTaskDialog
        open={isDeleteDialogOpen}
        onOpenChange={setIsDeleteDialogOpen}
        taskId={selectedTask?.id}
        taskName={`${selectedTask?.taskType} (${getTriggerTypeLabel(selectedTask?.triggerType as TriggerType)})`}
      />
    </>
  );
}
