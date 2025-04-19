import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import PageHead from "@/components/shared/page-head";
import { useDatabase } from "@/hooks/use-database";
import { TriggerType, TaskConfig } from "@/types/taskConfig";
import { PlusIcon, FileEditIcon, TrashIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { TaskForm } from "@/components/forms/task-form";
import { DeleteTaskDialog } from "@/components/modals/delete-task-dialog";
import { useState } from "react";

export default function OneTimeTasksPage() {
  const { taskConfigs, isLoading, error, refreshTaskConfigs } = useDatabase();
  
  // State for managing task forms and delete dialogs
  const [isCreateDialogOpen, setIsCreateDialogOpen] = useState(false);
  const [isEditDialogOpen, setIsEditDialogOpen] = useState(false);
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);
  const [selectedTask, setSelectedTask] = useState<TaskConfig | undefined>(undefined);

  // Filter tasks to show only interval-based tasks
  const oneTimeTasks = taskConfigs.filter(task => task.triggerType === TriggerType.Interval);

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

  return (
    <>
      <PageHead title="One-Time Tasks | CronPlus" />
      <div className="max-h-screen flex-1 space-y-4 overflow-y-auto p-4 pt-6 md:p-8">
        <div className="flex items-center justify-between space-y-2">
          <h2 className="text-3xl font-bold tracking-tight">One-Time Tasks</h2>
          <Button 
            onClick={handleCreateTask} 
            className="flex items-center gap-2"
          >
            <PlusIcon className="size-4" />
            Create One-Time Task
          </Button>
        </div>

        {error && (
          <Card className="border-red-500 bg-red-50 dark:bg-red-950/30">
            <CardHeader className="pb-2">
              <CardTitle className="text-red-600 dark:text-red-400">Error</CardTitle>
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
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {[1, 2, 3].map(i => (
              <Card key={i} className="animate-pulse">
                <CardHeader className="pb-2">
                  <div className="h-6 w-3/4 rounded-md bg-muted"></div>
                  <div className="h-4 w-1/2 rounded-md bg-muted"></div>
                </CardHeader>
                <CardContent>
                  <div className="h-20 rounded-md bg-muted"></div>
                </CardContent>
              </Card>
            ))}
          </div>
        ) : oneTimeTasks.length > 0 ? (
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {oneTimeTasks.map(task => (
              <Card key={task.id} className="hover:shadow-md transition-shadow">
                <CardHeader className="pb-2">
                  <div className="flex items-center justify-between">
                    <Badge className={getTaskTypeBadge(task.taskType)}>
                      {task.taskType}
                    </Badge>
                    <Badge variant="outline">
                      Interval: {task.interval || 0} seconds
                    </Badge>
                  </div>
                  <CardDescription className="mt-2">
                    ID: {task.id || "New Task"}
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="space-y-2 mb-4">
                    <p><span className="font-medium">Directory:</span> {task.directory}</p>
                    {task.sourceFile && (
                      <p><span className="font-medium">Source:</span> {task.sourceFile}</p>
                    )}
                    {task.destinationFile && (
                      <p><span className="font-medium">Destination:</span> {task.destinationFile}</p>
                    )}
                    {task.printerName && (
                      <p><span className="font-medium">Printer:</span> {task.printerName}</p>
                    )}
                    {task.archiveDirectory && (
                      <p><span className="font-medium">Archive:</span> {task.archiveDirectory}</p>
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
                      className="flex items-center gap-1 text-red-600 hover:text-red-700 hover:bg-red-50"
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
            <CardHeader className="pb-2">
              <CardTitle>No one-time tasks found</CardTitle>
              <CardDescription>
                Create your first one-time task to get started
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Button 
                variant="outline" 
                className="flex items-center gap-2"
                onClick={handleCreateTask}
              >
                <PlusIcon className="size-4" />
                Create One-Time Task
              </Button>
            </CardContent>
          </Card>
        )}
      </div>

      {/* Create Task Dialog - pre-set to Interval type for one-time tasks */}
      <TaskForm 
        open={isCreateDialogOpen}
        onOpenChange={setIsCreateDialogOpen}
        mode="create"
        initialData={{ triggerType: TriggerType.Interval } as TaskConfig}
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
        taskName={`${selectedTask?.taskType} (Interval: ${selectedTask?.interval || 0}s)`}
      />
    </>
  );
}

// Helper function to get the task type badge color
const getTaskTypeBadge = (taskType: string) => {
  const taskTypeColors: Record<string, string> = {
    "Copy": "bg-blue-500 hover:bg-blue-600",
    "Move": "bg-green-500 hover:bg-green-600",
    "Delete": "bg-red-500 hover:bg-red-600",
    "Print": "bg-purple-500 hover:bg-purple-600"
  };

  return taskTypeColors[taskType] || "bg-gray-500 hover:bg-gray-600";
};
