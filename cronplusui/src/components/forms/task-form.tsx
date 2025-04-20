import { useState, useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue
} from '@/components/ui/select';
import { useDatabase } from '@/hooks/use-database';
import { TaskConfig, TriggerType } from '@/types/taskConfig';

const taskFormSchema = z.object({
  triggerType: z.nativeEnum(TriggerType),
  sourceFolder: z.string().min(1, 'Source folder is required'),
  destinationFolder: z.string().min(1, 'Destination folder is required'),
  taskType: z.string().min(1, 'Task type is required'),
  sourceFile: z.string().optional(),
  destinationFile: z.string().optional(),
  time: z.string().optional(),
  interval: z.coerce.number().optional(),
  printerName: z.string().optional(),
  archiveDirectory: z.string().optional()
});

type TaskFormValues = z.infer<typeof taskFormSchema>;

interface TaskFormProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  initialData?: TaskConfig;
  mode: 'create' | 'edit';
}

export function TaskForm({
  open,
  onOpenChange,
  initialData,
  mode
}: TaskFormProps) {
  const { createTaskConfig, updateTaskConfig, refreshTaskConfigs } =
    useDatabase();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const form = useForm<TaskFormValues>({
    resolver: zodResolver(taskFormSchema),
    defaultValues: {
      triggerType: initialData?.triggerType || TriggerType.FileCreated,
      sourceFolder: initialData?.sourceFolder || '',
      destinationFolder: initialData?.destinationFolder || '',
      taskType: initialData?.taskType || '',
      sourceFile: initialData?.sourceFile || '',
      destinationFile: initialData?.destinationFile || '',
      time: initialData?.time || '',
      interval: initialData?.interval,
      printerName: initialData?.printerName || '',
      archiveDirectory: initialData?.archiveDirectory || ''
    }
  });

  // Reset form when initialData changes
  useEffect(() => {
    if (initialData) {
      form.reset({
        triggerType: initialData.triggerType,
        sourceFolder: initialData.sourceFolder,
        destinationFolder: initialData.destinationFolder,
        taskType: initialData.taskType,
        sourceFile: initialData.sourceFile || '',
        destinationFile: initialData.destinationFile || '',
        time: initialData.time || '',
        interval: initialData.interval,
        printerName: initialData.printerName || '',
        archiveDirectory: initialData.archiveDirectory || ''
      });
    } else {
      form.reset({
        triggerType: TriggerType.FileCreated,
        sourceFolder: '',
        destinationFolder: '',
        taskType: '',
        sourceFile: '',
        destinationFile: '',
        time: '',
        interval: undefined,
        printerName: '',
        archiveDirectory: ''
      });
    }
    // Clear any previous errors when the form is reset
    setError(null);
  }, [initialData, form]);

  // Handle form submission
  const onSubmit = async (values: TaskFormValues) => {
    setIsSubmitting(true);
    setError(null);

    try {
      let success = false;

      if (mode === 'create') {
        const result = await createTaskConfig(values);
        success = !!result;
      } else if (mode === 'edit' && initialData?.id) {
        const result = await updateTaskConfig(initialData.id, values);
        success = !!result;
      }

      if (success) {
        // Explicitly refresh the task list to update the UI
        await refreshTaskConfigs();
        onOpenChange(false);
      } else {
        setError('Failed to save task. Please try again.');
      }
    } catch (error) {
      console.error('Error saving task:', error);
      setError(
        `Error: ${error instanceof Error ? error.message : 'Unknown error occurred'}`
      );
    } finally {
      setIsSubmitting(false);
    }
  };

  // Handle displaying different fields based on trigger type
  const triggerType = form.watch('triggerType');

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[600px]">
        <DialogHeader>
          <DialogTitle>
            {mode === 'create' ? 'Create Task' : 'Edit Task'}
          </DialogTitle>
          <DialogDescription>
            {mode === 'create'
              ? 'Create a new task configuration'
              : 'Edit the existing task configuration'}
          </DialogDescription>
        </DialogHeader>

        {error && (
          <div className="mb-4 rounded border border-red-400 bg-red-50 px-4 py-3 text-red-700">
            <p>{error}</p>
          </div>
        )}

        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-6">
            <FormField
              control={form.control}
              name="triggerType"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Trigger Type</FormLabel>
                  <Select
                    disabled={isSubmitting}
                    onValueChange={field.onChange}
                    defaultValue={field.value}
                    value={field.value}
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder="Select trigger type" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      <SelectItem value={TriggerType.FileCreated}>
                        File Created
                      </SelectItem>
                      <SelectItem value={TriggerType.FileRenamed}>
                        File Renamed
                      </SelectItem>
                      <SelectItem value={TriggerType.Time}>
                        Schedule (Cron)
                      </SelectItem>
                      <SelectItem value={TriggerType.Interval}>
                        Interval
                      </SelectItem>
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="taskType"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Task Type</FormLabel>
                  <Select
                    disabled={isSubmitting}
                    onValueChange={field.onChange}
                    defaultValue={field.value}
                    value={field.value}
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder="Select task type" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      <SelectItem value="Copy">Copy</SelectItem>
                      <SelectItem value="Move">Move</SelectItem>
                      <SelectItem value="Delete">Delete</SelectItem>
                      <SelectItem value="Print">Print</SelectItem>
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="sourceFolder"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Source Folder</FormLabel>
                  <FormControl>
                    <Input
                      disabled={isSubmitting}
                      placeholder="/path/to/source/folder"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="destinationFolder"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Destination Folder</FormLabel>
                  <FormControl>
                    <Input
                      disabled={isSubmitting}
                      placeholder="/path/to/destination/folder"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            {triggerType === TriggerType.Time && (
              <FormField
                control={form.control}
                name="time"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Schedule (Cron Expression)</FormLabel>
                    <FormControl>
                      <Input
                        disabled={isSubmitting}
                        placeholder="* * * * *"
                        {...field}
                      />
                    </FormControl>
                    <FormDescription>
                      Cron expression format: minute hour day month weekday
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />
            )}

            {triggerType === TriggerType.Interval && (
              <FormField
                control={form.control}
                name="interval"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Interval (seconds)</FormLabel>
                    <FormControl>
                      <Input
                        disabled={isSubmitting}
                        type="number"
                        min="1"
                        placeholder="60"
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            )}

            {/* Source File - for File triggers or Copy/Move operations */}
            {(triggerType === TriggerType.FileCreated ||
              triggerType === TriggerType.FileRenamed ||
              form.watch('taskType') === 'Copy' ||
              form.watch('taskType') === 'Move' ||
              form.watch('taskType') === 'Print') && (
              <FormField
                control={form.control}
                name="sourceFile"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Source File</FormLabel>
                    <FormControl>
                      <Input
                        disabled={isSubmitting}
                        placeholder="file.txt or *.pdf"
                        {...field}
                      />
                    </FormControl>
                    <FormDescription>
                      File name or pattern to match
                    </FormDescription>
                    <FormMessage />
                  </FormItem>
                )}
              />
            )}

            {/* Destination File - for Copy/Move operations */}
            {(form.watch('taskType') === 'Copy' ||
              form.watch('taskType') === 'Move') && (
              <FormField
                control={form.control}
                name="destinationFile"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Destination File</FormLabel>
                    <FormControl>
                      <Input
                        disabled={isSubmitting}
                        placeholder="/path/to/destination.txt"
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            )}

            {/* Printer Name - for Print operation */}
            {form.watch('taskType') === 'Print' && (
              <FormField
                control={form.control}
                name="printerName"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Printer Name</FormLabel>
                    <FormControl>
                      <Input
                        disabled={isSubmitting}
                        placeholder="Default Printer"
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            )}

            {/* Archive Directory - optional for multiple operations */}
            <FormField
              control={form.control}
              name="archiveDirectory"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Archive Directory (Optional)</FormLabel>
                  <FormControl>
                    <Input
                      disabled={isSubmitting}
                      placeholder="/path/to/archive"
                      {...field}
                    />
                  </FormControl>
                  <FormDescription>
                    Optional directory to archive processed files
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <DialogFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() => onOpenChange(false)}
                disabled={isSubmitting}
              >
                Cancel
              </Button>
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting
                  ? 'Saving...'
                  : mode === 'create'
                    ? 'Create'
                    : 'Save Changes'}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
